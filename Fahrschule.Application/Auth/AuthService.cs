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
/// Ergebnis einer erfolgreichen Anmeldung bzw. Token-Erneuerung.
/// Die Tokens selbst wandern im Controller in httpOnly-Cookies –
/// der Service kennt bewusst keine Cookies (Trennung der Schichten:
/// HTTP-Details gehören in die API-Schicht, Fachlogik hierher).
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
/// Die Fachlogik der Anmeldung (siehe KONZEPT "Sicherheit &amp; Anmeldung").
///
/// Ablauf Login: E-Mail+Passwort prüfen (mit Konto-Sperre nach Fehlversuchen),
/// dann kurzlebiges Zugriffstoken (JWT) + langlebiges Refresh-Token ausstellen.
/// Vom Refresh-Token speichern wir nur den Hash (wie bei Passwörtern).
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

    // Bewusst EINE gemeinsame Meldung für "E-Mail unbekannt" und "Passwort falsch":
    // sonst könnte ein Angreifer durchprobieren, welche E-Mail-Adressen ein Konto haben.
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
            // Fehlversuch zählen – nach 5 Versuchen sperrt Identity das Konto automatisch.
            await userManager.AccessFailedAsync(user);
            throw new AuthenticationFailedException(LoginFailedMessage);
        }

        // Erfolgreich angemeldet → Fehlversuchszähler zurücksetzen.
        await userManager.ResetAccessFailedCountAsync(user);

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
            // Auch hier eine bewusst knappe Meldung – das Frontend leitet dann zur Anmeldung.
            throw new AuthenticationFailedException("Die Sitzung ist abgelaufen. Bitte melden Sie sich neu an.");
        }

        // Rotation: Das alte Token wird entwertet und durch ein neues ersetzt.
        // Ein gestohlenes, bereits eingelöstes Token ist damit wertlos.
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
        // Kein Fehler, wenn das Token unbekannt ist – Abmelden soll immer "klappen".
    }

    public async Task<AuthResult> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new AuthenticationFailedException("Die Sitzung ist abgelaufen. Bitte melden Sie sich neu an.");

        var result = await userManager.ChangePasswordAsync(user, currentPassword, newPassword);
        if (!result.Succeeded)
        {
            // Identity liefert die Gründe (z. B. Passwort zu kurz) – wir geben sie
            // gesammelt und verständlich zurück.
            var reasons = string.Join(" ", result.Errors.Select(TranslateIdentityError));
            throw new AppValidationException(reasons);
        }

        // Temporäres Passwort ist hiermit Geschichte.
        user.MustChangePassword = false;
        await userManager.UpdateAsync(user);

        // Sicherheit: Passwortwechsel meldet alle anderen Geräte ab
        // (alle Refresh-Tokens entwerten), danach bekommt DIESES Gerät neue Tokens.
        var now = DateTime.UtcNow;
        await db.RefreshTokens
            .Where(t => t.UserId == user.Id && t.RevokedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAtUtc, now), ct);

        // Ins Audit-Log – DASS geändert wurde, nie WAS (keine Passwörter ins Log!).
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

    /// <summary>Stellt ein frisches Token-Paar aus und merkt sich den Refresh-Hash.</summary>
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

    /// <summary>Übersetzt die englischen Identity-Fehlercodes in einfaches Deutsch.</summary>
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
