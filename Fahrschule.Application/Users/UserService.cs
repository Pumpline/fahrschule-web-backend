using Fahrschule.Application.Audit;
using Fahrschule.Application.Common;
using Fahrschule.Application.LicenseClasses;
using Fahrschule.Contracts.Users;
using Fahrschule.Infrastructure.Identity;
using Fahrschule.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Fahrschule.Application.Users;

public interface IUserService
{
    Task<List<UserDto>> GetAllAsync(CancellationToken ct = default);
    Task<TemporaryPasswordResultDto> CreateAsync(CreateUserRequest request, Actor actor, CancellationToken ct = default);
    Task<UserDto> UpdateAsync(Guid id, UpdateUserRequest request, Actor actor, CancellationToken ct = default);
    Task<TemporaryPasswordResultDto> ResetPasswordAsync(Guid id, Actor actor, CancellationToken ct = default);
    Task DeleteAsync(Guid id, Actor actor, CancellationToken ct = default);
}

/// <summary>
/// Business logic for user management in the admin panel (KONZEPT 3.7a).
///
/// New users receive a generated TEMPORARY password (returned once so the
/// admin can pass it on) and must set their own on first sign-in
/// (MustChangePassword). Password reset works the same way - no e-mail needed.
///
/// Safety rails: an admin cannot delete their own account and the LAST
/// remaining admin can neither be deleted nor demoted - otherwise nobody
/// could ever reach the admin panel again.
///
/// User management touches personal staff data → every change is audited
/// (never the passwords).
/// </summary>
public class UserService(
    UserManager<ApplicationUser> userManager,
    FahrschuleDbContext db,
    IAuditWriter auditWriter) : IUserService
{
    public async Task<List<UserDto>> GetAllAsync(CancellationToken ct = default)
    {
        var users = await userManager.Users
            .OrderBy(u => u.DisplayName)
            .ToListAsync(ct);

        var result = new List<UserDto>(users.Count);
        foreach (var user in users)
        {
            result.Add(await ToDtoAsync(user));
        }
        return result;
    }

    public async Task<TemporaryPasswordResultDto> CreateAsync(CreateUserRequest request, Actor actor, CancellationToken ct = default)
    {
        var role = ValidateRole(request.Role);
        var email = request.Email.Trim();
        var displayName = request.DisplayName.Trim();

        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new AppValidationException("Bitte einen Namen eintragen.");
        }

        var temporaryPassword = TemporaryPasswordGenerator.Generate();
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = displayName,
            CreatedAtUtc = DateTime.UtcNow,
            MustChangePassword = true, // forced change on first sign-in
        };

        var createResult = await userManager.CreateAsync(user, temporaryPassword);
        if (!createResult.Succeeded)
        {
            throw new AppValidationException(TranslateErrors(createResult));
        }

        await userManager.AddToRoleAsync(user, role);

        await auditWriter.WriteAsync(actor.UserId, actor.UserName, "Angelegt",
            "Benutzer", email, newValuesJson: $"{{\"Rolle\":\"{role}\"}}", cancellationToken: ct);

        return new TemporaryPasswordResultDto
        {
            User = await ToDtoAsync(user),
            TemporaryPassword = temporaryPassword,
        };
    }

    public async Task<UserDto> UpdateAsync(Guid id, UpdateUserRequest request, Actor actor, CancellationToken ct = default)
    {
        var user = await FindOrThrowAsync(id);
        var newRole = ValidateRole(request.Role);
        var displayName = request.DisplayName.Trim();
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new AppValidationException("Bitte einen Namen eintragen.");
        }

        var oldRole = await GetRoleAsync(user);

        // Safety: do not demote the last remaining admin.
        if (oldRole == Roles.Admin && newRole != Roles.Admin)
        {
            await EnsureNotLastAdminAsync(user.Id);
        }

        user.DisplayName = displayName;
        await userManager.UpdateAsync(user);

        if (oldRole != newRole)
        {
            if (!string.IsNullOrEmpty(oldRole))
            {
                await userManager.RemoveFromRoleAsync(user, oldRole);
            }
            await userManager.AddToRoleAsync(user, newRole);
        }

        await auditWriter.WriteAsync(actor.UserId, actor.UserName, "Geändert",
            "Benutzer", user.Email ?? user.Id.ToString(),
            oldValuesJson: $"{{\"Rolle\":\"{oldRole}\"}}",
            newValuesJson: $"{{\"Rolle\":\"{newRole}\"}}", cancellationToken: ct);

        return await ToDtoAsync(user);
    }

    public async Task<TemporaryPasswordResultDto> ResetPasswordAsync(Guid id, Actor actor, CancellationToken ct = default)
    {
        var user = await FindOrThrowAsync(id);

        var temporaryPassword = TemporaryPasswordGenerator.Generate();

        // Admin-initiated reset (no e-mail token needed): remove the old
        // password and set the new temporary one directly. This deliberately
        // avoids GeneratePasswordResetTokenAsync, which would require a
        // registered token provider (we use AddIdentityCore without one).
        if (await userManager.HasPasswordAsync(user))
        {
            var removeResult = await userManager.RemovePasswordAsync(user);
            if (!removeResult.Succeeded)
            {
                throw new AppValidationException(TranslateErrors(removeResult));
            }
        }

        var addResult = await userManager.AddPasswordAsync(user, temporaryPassword);
        if (!addResult.Succeeded)
        {
            throw new AppValidationException(TranslateErrors(addResult));
        }

        // Force a new own password again, clear any lockout, and sign out all
        // devices (revoke refresh tokens) for safety.
        user.MustChangePassword = true;
        await userManager.UpdateAsync(user);
        await userManager.SetLockoutEndDateAsync(user, null);
        await userManager.ResetAccessFailedCountAsync(user);

        var now = DateTime.UtcNow;
        await db.RefreshTokens
            .Where(t => t.UserId == user.Id && t.RevokedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAtUtc, now), ct);

        await auditWriter.WriteAsync(actor.UserId, actor.UserName, "PasswortZurückgesetzt",
            "Benutzer", user.Email ?? user.Id.ToString(), cancellationToken: ct);

        return new TemporaryPasswordResultDto
        {
            User = await ToDtoAsync(user),
            TemporaryPassword = temporaryPassword,
        };
    }

    public async Task DeleteAsync(Guid id, Actor actor, CancellationToken ct = default)
    {
        var user = await FindOrThrowAsync(id);

        if (user.Id == actor.UserId)
        {
            throw new AppValidationException("Sie können Ihr eigenes Konto nicht löschen.");
        }

        if (await GetRoleAsync(user) == Roles.Admin)
        {
            await EnsureNotLastAdminAsync(user.Id);
        }

        var email = user.Email ?? user.Id.ToString();
        var deleteResult = await userManager.DeleteAsync(user);
        if (!deleteResult.Succeeded)
        {
            throw new AppValidationException(TranslateErrors(deleteResult));
        }

        // Audit keeps the name as a copy, so the history stays readable.
        await auditWriter.WriteAsync(actor.UserId, actor.UserName, "Gelöscht",
            "Benutzer", email, cancellationToken: ct);
    }

    /// <summary>Throws if removing/demoting this admin would leave no admin at all.</summary>
    private async Task EnsureNotLastAdminAsync(Guid userIdBeingChanged)
    {
        var admins = await userManager.GetUsersInRoleAsync(Roles.Admin);
        var remaining = admins.Count(a => a.Id != userIdBeingChanged);
        if (remaining == 0)
        {
            throw new AppValidationException(
                "Das ist der einzige Administrator. Legen Sie zuerst einen weiteren Admin an, " +
                "bevor Sie diesen ändern oder löschen.");
        }
    }

    private static string ValidateRole(string? role)
    {
        var trimmed = (role ?? string.Empty).Trim();
        if (!Roles.All.Contains(trimmed))
        {
            throw new AppValidationException("Bitte eine gültige Rolle wählen (Admin, Fahrlehrer oder Verwaltung).");
        }
        return trimmed;
    }

    private async Task<ApplicationUser> FindOrThrowAsync(Guid id)
        => await userManager.FindByIdAsync(id.ToString())
            ?? throw new NotFoundException("Dieser Benutzer wurde nicht gefunden. Bitte die Liste neu laden.");

    private async Task<string> GetRoleAsync(ApplicationUser user)
        => (await userManager.GetRolesAsync(user)).FirstOrDefault() ?? string.Empty;

    private async Task<UserDto> ToDtoAsync(ApplicationUser user) => new()
    {
        Id = user.Id,
        Email = user.Email ?? string.Empty,
        DisplayName = user.DisplayName,
        Role = await GetRoleAsync(user),
        MustChangePassword = user.MustChangePassword,
        IsLockedOut = await userManager.IsLockedOutAsync(user),
        LastLoginAtUtc = user.LastLoginAtUtc,
    };

    /// <summary>Translates Identity error codes into plain German (user-facing).</summary>
    private static string TranslateErrors(IdentityResult result)
        => string.Join(" ", result.Errors.Select(e => e.Code switch
        {
            "DuplicateUserName" or "DuplicateEmail" => "Diese E-Mail-Adresse wird bereits verwendet.",
            "InvalidEmail" => "Das ist keine gültige E-Mail-Adresse.",
            "PasswordTooShort" => "Das Passwort ist zu kurz.",
            _ => e.Description,
        }));
}
