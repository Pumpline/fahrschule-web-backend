using Fahrschule.Application.Common;
using Fahrschule.Application.LicenseClasses;
using Fahrschule.Contracts.Admin;
using Fahrschule.Domain.Entities;
using Fahrschule.Infrastructure.Identity;
using Fahrschule.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fahrschule.Application.Audit;

/// <summary>
/// Decides which audit categories each role may see in the change log, and lets
/// the Admin configure that in the admin panel (KONZEPT 1/4: least privilege -
/// the office and instructors should not see e.g. password changes).
///
/// The configuration is stored as two free-text rows in the generic Setting
/// table (one comma-separated key list per editable role); a missing row falls
/// back to the sensible defaults below, so no seeding/migration is required.
/// Admin always sees every category and is never stored here.
/// </summary>
public interface IAuditVisibilityService
{
    /// <summary>The category keys a user with these roles may see. Admin → all.</summary>
    Task<IReadOnlySet<string>> AllowedCategoriesAsync(IEnumerable<string> roles, CancellationToken ct = default);

    /// <summary>The full configuration for the admin matrix.</summary>
    Task<AuditVisibilityDto> GetConfigAsync(CancellationToken ct = default);

    /// <summary>Save the role→category visibility (admin only). Audited.</summary>
    Task SaveConfigAsync(AuditVisibilityDto config, Actor actor, CancellationToken ct = default);
}

public class AuditVisibilityService(FahrschuleDbContext db, IAuditWriter auditWriter) : IAuditVisibilityService
{
    private const string KeyPrefix = "Audit.Visible.";

    // The two roles whose visibility the Admin may restrict. Admin is omitted on
    // purpose (always sees everything).
    private static readonly string[] EditableRoles = [Roles.Fahrlehrer, Roles.Verwaltung];

    // Sensible defaults (used when no setting row exists yet):
    // - Fahrlehrer: their daily work - students, training, appointments.
    // - Verwaltung: the same plus the setup/master-data area they maintain.
    private static readonly Dictionary<string, string[]> Defaults = new()
    {
        [Roles.Fahrlehrer] = [AuditCategory.Students, AuditCategory.Training, AuditCategory.Calendar],
        [Roles.Verwaltung] = [AuditCategory.Students, AuditCategory.Training, AuditCategory.Calendar, AuditCategory.Setup],
    };

    public async Task<IReadOnlySet<string>> AllowedCategoriesAsync(IEnumerable<string> roles, CancellationToken ct = default)
    {
        var roleList = roles.ToList();
        if (roleList.Contains(Roles.Admin))
        {
            return AuditCategory.All.Select(c => c.Key).ToHashSet();
        }

        var stored = await LoadStoredAsync(ct);
        var allowed = new HashSet<string>();
        foreach (var role in roleList.Where(EditableRoles.Contains))
        {
            allowed.UnionWith(stored.TryGetValue(role, out var keys) ? keys : (IEnumerable<string>)Defaults[role]);
        }
        return allowed;
    }

    public async Task<AuditVisibilityDto> GetConfigAsync(CancellationToken ct = default)
    {
        var stored = await LoadStoredAsync(ct);
        return new AuditVisibilityDto
        {
            Categories = AuditCategory.All.Select(c => new AuditCategoryDto { Key = c.Key, Label = c.Label }).ToList(),
            Roles = EditableRoles.Select(role => new AuditRoleVisibilityDto
            {
                Role = role,
                Categories = (stored.TryGetValue(role, out var keys)
                    ? keys
                    : (IEnumerable<string>)Defaults[role]).ToList(),
            }).ToList(),
        };
    }

    public async Task SaveConfigAsync(AuditVisibilityDto config, Actor actor, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        foreach (var role in EditableRoles)
        {
            // Keep only known category keys (ignore anything unexpected from the client).
            var keys = config.Roles.FirstOrDefault(r => r.Role == role)?.Categories ?? [];
            var clean = keys.Where(AuditCategory.IsKnown).Distinct().ToList();

            var key = KeyPrefix + role;
            var existing = await db.Settings.FirstOrDefaultAsync(s => s.Key == key, ct);
            var value = string.Join(",", clean);
            if (existing is null)
            {
                db.Settings.Add(new Setting
                {
                    Key = key,
                    Value = value,
                    Description = $"Sichtbare Protokoll-Kategorien für Rolle {role}",
                    UpdatedAtUtc = now,
                });
            }
            else
            {
                existing.Value = value;
                existing.UpdatedAtUtc = now;
            }
        }

        await db.SaveChangesAsync(ct);
        await auditWriter.WriteAsync(actor.UserId, actor.UserName, "Geändert",
            "Einstellungen", "Protokoll-Sichtbarkeit", cancellationToken: ct);
    }

    /// <summary>Reads the stored category lists per editable role (key → set).</summary>
    private async Task<Dictionary<string, HashSet<string>>> LoadStoredAsync(CancellationToken ct)
    {
        var keys = EditableRoles.Select(r => KeyPrefix + r).ToList();
        var rows = await db.Settings.Where(s => keys.Contains(s.Key)).ToListAsync(ct);

        var result = new Dictionary<string, HashSet<string>>();
        foreach (var role in EditableRoles)
        {
            var row = rows.FirstOrDefault(r => r.Key == KeyPrefix + role);
            if (row is not null)
            {
                result[role] = row.Value
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(AuditCategory.IsKnown)
                    .ToHashSet();
            }
        }
        return result;
    }
}
