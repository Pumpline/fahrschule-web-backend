using Fahrschule.Application.Audit;
using Fahrschule.Application.Common;
using Fahrschule.Contracts.Auth;
using Fahrschule.Infrastructure.Identity;
using Fahrschule.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Fahrschule.Application.Auth;

/// <summary>
/// Result of a successful sign-in or token refresh.
/// The tokens themselves are placed into httpOnly cookies by the controller -
/// the service deliberately knows nothing about cookies (layer separation:
/// HTTP details belong to the API layer, business logic lives here).
/// </summary>
public record AuthResult(
    string AccessToken,
    DateTime AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTime RefreshTokenExpiresAtUtc,
    CurrentUserDto User);

public interface IAuthService
{
    Task<AuthResult> LoginAsync(string email, string password, CancellationToken ct = default);
    Task<AuthResult> RefreshAsync(string refreshToken, CancellationToken ct = default);
    Task LogoutAsync(string refreshToken, CancellationToken ct = default);
    Task<AuthResult> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken ct = default);
    Task<CurrentUserDto> GetCurrentUserAsync(Guid userId, CancellationToken ct = default);
}

/// <summary>
/// The sign-in business logic (see KONZEPT "Sicherheit &amp; Anmeldung").
///
/// Login flow: verify e-mail + password (with account lockout after failed
/// attempts), then issue a short-lived access token (JWT) plus a long-lived
/// refresh token. Of the refresh token we only store the hash.
/// </summary>
public class AuthService(
    UserManager<ApplicationUser> userManager,
    IJwtTokenService jwtTokenService,
    FahrschuleDbContext db,
    IAuditWriter auditWriter,
    IOptions<JwtOptions> jwtOptions,
    ILogger<AuthService> logger) : IAuthService
{
    private readonly JwtOptions _jwtOptions = jwtOptions.Value;

    // Deliberately ONE shared message for "unknown e-mail" and "wrong password":
    // otherwise an attacker could probe which e-mail addresses have an account.
    private const string LoginFailedMessage = "E-Mail oder Passwort ist falsch. Bitte prüfen Sie beides und versuchen Sie es noch einmal.";

    public async Task<AuthResult> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            throw new AuthenticationFailedException(LoginFailedMessage);
        }

        if (await userManager.IsLockedOutAsync(user))
        {
            throw new AuthenticationFailedException(
                "Das Konto ist nach mehreren Fehlversuchen vorübergehend gesperrt. " +
                "Bitte warten Sie 15 Minuten und versuchen Sie es dann erneut.");
        }

        if (!await userManager.CheckPasswordAsync(user, password))
        {
            // Count the failed attempt - after 5, Identity locks the account automatically.
            await userManager.AccessFailedAsync(user);
            throw new AuthenticationFailedException(LoginFailedMessage);
        }

        // Successful sign-in → reset the failed-attempts counter and remember
        // the time (shown in the admin user list to spot unused accounts).
        await userManager.ResetAccessFailedCountAsync(user);
        user.LastLoginAtUtc = DateTime.UtcNow;
        await userManager.UpdateAsync(user);

        logger.LogInformation("Benutzer {UserId} hat sich angemeldet.", user.Id);
        return await IssueTokensAsync(user, ct);
    }

    public async Task<AuthResult> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        var tokenHash = TokenHasher.Hash(refreshToken);
        var stored = await db.RefreshTokens
            .Include(t => t.User)
            .SingleOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

        if (stored is null || stored.User is null || !stored.IsActive(DateTime.UtcNow))
        {
            // Again a deliberately terse message - the frontend redirects to sign-in.
            throw new AuthenticationFailedException("Die Sitzung ist abgelaufen. Bitte melden Sie sich neu an.");
        }

        // Rotation: the old token is invalidated and replaced by a new one.
        // A stolen, already-redeemed token is therefore worthless.
        var result = await IssueTokensAsync(stored.User, ct);
        stored.RevokedAtUtc = DateTime.UtcNow;
        stored.ReplacedByTokenHash = TokenHasher.Hash(result.RefreshToken);
        await db.SaveChangesAsync(ct);

        return result;
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken ct = default)
    {
        var tokenHash = TokenHasher.Hash(refreshToken);
        var stored = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == tokenHash, ct);
        if (stored is not null && stored.RevokedAtUtc is null)
        {
            stored.RevokedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        // No error for unknown tokens - logging out should always "work".
    }

    public async Task<AuthResult> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new AuthenticationFailedException("Die Sitzung ist abgelaufen. Bitte melden Sie sich neu an.");

        var result = await userManager.ChangePasswordAsync(user, currentPassword, newPassword);
        if (!result.Succeeded)
        {
            // Identity reports the reasons (e.g. password too short) - we
            // return them collected and in plain German.
            var reasons = string.Join(" ", result.Errors.Select(TranslateIdentityError));
            throw new AppValidationException(reasons);
        }

        // The temporary password is history now.
        user.MustChangePassword = false;
        await userManager.UpdateAsync(user);

        // Security: a password change signs out all other devices
        // (revoke all refresh tokens); THIS device gets fresh tokens below.
        var now = DateTime.UtcNow;
        await db.RefreshTokens
            .Where(t => t.UserId == user.Id && t.RevokedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAtUtc, now), ct);

        // Audit THAT it changed, never WHAT (no passwords in the log!).
        await auditWriter.WriteAsync(
            user.Id, user.DisplayName, "PasswortGeändert", "Benutzer", user.Id.ToString(),
            cancellationToken: ct);

        return await IssueTokensAsync(user, ct);
    }

    public async Task<CurrentUserDto> GetCurrentUserAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new AuthenticationFailedException("Die Sitzung ist abgelaufen. Bitte melden Sie sich neu an.");

        return await ToDtoAsync(user);
    }

    /// <summary>Issues a fresh token pair and stores the refresh hash.</summary>
    private async Task<AuthResult> IssueTokensAsync(ApplicationUser user, CancellationToken ct)
    {
        var roles = await userManager.GetRolesAsync(user);
        var (accessToken, accessExpires) = jwtTokenService.CreateAccessToken(user, roles);

        var refreshToken = TokenHasher.GenerateRefreshToken();
        var refreshExpires = DateTime.UtcNow.AddDays(_jwtOptions.RefreshTokenDays);

        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = TokenHasher.Hash(refreshToken),
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = refreshExpires,
        });
        await db.SaveChangesAsync(ct);

        var dto = await ToDtoAsync(user);
        return new AuthResult(accessToken, accessExpires, refreshToken, refreshExpires, dto);
    }

    private async Task<CurrentUserDto> ToDtoAsync(ApplicationUser user)
    {
        var roles = await userManager.GetRolesAsync(user);
        return new CurrentUserDto
        {
            Id = user.Id,
            Email = user.Email ?? string.Empty,
            DisplayName = user.DisplayName,
            Roles = [.. roles],
            MustChangePassword = user.MustChangePassword,
        };
    }

    /// <summary>Translates the English Identity error codes into plain German (user-facing).</summary>
    private static string TranslateIdentityError(IdentityError error) => error.Code switch
    {
        "PasswordTooShort" => "Das neue Passwort muss mindestens 10 Zeichen lang sein.",
        "PasswordRequiresDigit" => "Das neue Passwort braucht mindestens eine Ziffer (0–9).",
        "PasswordRequiresUpper" => "Das neue Passwort braucht mindestens einen Großbuchstaben.",
        "PasswordRequiresLower" => "Das neue Passwort braucht mindestens einen Kleinbuchstaben.",
        "PasswordMismatch" => "Das aktuelle Passwort ist nicht richtig.",
        _ => error.Description,
    };
}
