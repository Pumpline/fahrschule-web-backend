using Fahrschule.Contracts.Admin;
using Fahrschule.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fahrschule.Application.Audit;

public interface IAuditQueryService
{
    Task<AuditListResultDto> GetListAsync(
        IEnumerable<string> roles, string? search, string? category,
        int page, int pageSize, CancellationToken ct = default);
}

/// <summary>
/// Read access to the audit log for the change-log page (KONZEPT 3.7): "who
/// changed what, when". The log is append-only (see AuditWriter) - this service
/// never modifies it. It also:
///  - filters entries to the categories the current role may see (role visibility),
///  - resolves the initiator's CURRENT display name (so a later rename shows up),
///  - resolves the affected student's current name + id (for a link to the file).
/// </summary>
public class AuditQueryService(FahrschuleDbContext db, IAuditVisibilityService visibility) : IAuditQueryService
{
    private const int MaxPageSize = 100;

    // Entry types whose EntityId starts with the student's id (optionally followed
    // by "/title"). Used to show the student's name with a link to their file.
    private static readonly HashSet<string> StudentEntityTypes =
    [
        "Schüler", "Ausbildungsfortschritt", "Ausbildungsstunde", "Prüfung", "Unterlage-Schüler",
    ];

    public async Task<AuditListResultDto> GetListAsync(
        IEnumerable<string> roles, string? search, string? category,
        int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize <= 0 ? 20 : pageSize, 1, MaxPageSize);

        var allowed = await visibility.AllowedCategoriesAsync(roles, ct);

        var query = db.AuditLogs.Where(a => allowed.Contains(a.Category));

        // Optional single-category filter (only honoured if the role may see it).
        if (!string.IsNullOrWhiteSpace(category) && allowed.Contains(category))
        {
            query = query.Where(a => a.Category == category);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(a =>
                EF.Functions.ILike(a.UserName, $"%{term}%") ||
                EF.Functions.ILike(a.Action, $"%{term}%") ||
                EF.Functions.ILike(a.EntityType, $"%{term}%") ||
                EF.Functions.ILike(a.EntityId, $"%{term}%"));
        }

        var total = await query.CountAsync(ct);

        // One DB read for the page; names are resolved in memory afterwards.
        var pageRows = await query
            .OrderByDescending(a => a.TimestampUtc).ThenByDescending(a => a.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new
            {
                a.Id, a.TimestampUtc, a.UserId, a.UserName, a.Action,
                a.EntityType, a.EntityId, a.OldValuesJson, a.NewValuesJson, a.Category,
            })
            .ToListAsync(ct);

        // Current display names of the initiators (so a later rename is reflected).
        var userIds = pageRows.Where(r => r.UserId != null).Select(r => r.UserId!.Value).Distinct().ToList();
        var userNames = await db.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

        // Current names of referenced students (IgnoreQueryFilters: still resolve
        // soft-deleted ones, so the log stays readable).
        var studentIds = pageRows
            .Where(r => StudentEntityTypes.Contains(r.EntityType))
            .Select(r => LeadingStudentId(r.EntityId))
            .Where(id => id != null).Select(id => id!.Value).Distinct().ToList();
        var students = await db.Students
            .IgnoreQueryFilters()
            .Where(s => studentIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => $"{s.FirstName} {s.LastName}".Trim(), ct);

        var rows = pageRows.Select(r =>
        {
            var dto = new AuditLogDto
            {
                Id = r.Id,
                TimestampUtc = r.TimestampUtc,
                UserName = r.UserId != null && userNames.TryGetValue(r.UserId.Value, out var current)
                    ? current : r.UserName,
                Action = r.Action,
                EntityType = r.EntityType,
                EntityId = r.EntityId,
                OldValuesJson = r.OldValuesJson,
                NewValuesJson = r.NewValuesJson,
                Category = r.Category,
                CategoryLabel = AuditCategory.Label(r.Category),
                Detail = ExtractDetail(r.EntityId, r.OldValuesJson, r.NewValuesJson),
            };

            if (StudentEntityTypes.Contains(r.EntityType))
            {
                var sid = LeadingStudentId(r.EntityId);
                if (sid != null && students.TryGetValue(sid.Value, out var name))
                {
                    dto.StudentId = sid;
                    dto.StudentName = name;
                }
            }
            return dto;
        }).ToList();

        var categories = allowed
            .Select(key => new { key, order = OrderOf(key) })
            .OrderBy(x => x.order)
            .Select(x => new AuditCategoryDto { Key = x.key, Label = AuditCategory.Label(x.key) })
            .ToList();

        return new AuditListResultDto
        {
            Items = rows, Total = total, Page = page, PageSize = pageSize, Categories = categories,
        };
    }

    /// <summary>German labels for the snapshot field keys, so a "Geändert" entry
    /// can say WHICH fields changed (names only, never values - data minimisation).
    /// Unknown keys fall back to the raw key.</summary>
    private static readonly Dictionary<string, string> FieldLabels = new()
    {
        ["FirstName"] = "Vorname", ["LastName"] = "Nachname", ["DateOfBirth"] = "Geburtsdatum",
        ["Email"] = "E-Mail", ["Phone"] = "Telefon", ["Address"] = "Adresse", ["Notes"] = "Notizen",
        ["Code"] = "Kürzel", ["Description"] = "Beschreibung", ["MinimumAge"] = "Mindestalter",
        ["Requirements"] = "Voraussetzungen", ["IsActive"] = "Aktiv-Status", ["SortOrder"] = "Reihenfolge",
        ["RequiredTheoryDoubleLessons"] = "Theorie-Doppelstunden",
        ["RequiredSpecialDrivesOverland"] = "Sonderfahrten Überland",
        ["RequiredSpecialDrivesHighway"] = "Sonderfahrten Autobahn",
        ["RequiredSpecialDrivesNight"] = "Sonderfahrten Nacht",
        ["Section"] = "Abschnitt", ["Title"] = "Bezeichnung", ["RequiredCount"] = "Soll-Anzahl",
        ["Role"] = "Rolle", ["DisplayName"] = "Name",
    };

    /// <summary>
    /// Extracts a human-readable specific of the action so the log reads precisely:
    ///  - an EntityId of the form "guid/rest" carries the affected item in "rest"
    ///    (e.g. the plan point "Überlandfahrt" or the document name),
    ///  - a NewValuesJson of {"Feld":"E-Mail"} carries the viewed/changed field,
    ///  - otherwise the before/after snapshots are diffed to list the CHANGED field
    ///    names (no values, so the log stays data-minimal).
    /// Returns null when there is nothing extra to show.
    /// </summary>
    private static string? ExtractDetail(string entityId, string? oldValuesJson, string? newValuesJson)
    {
        var slash = entityId.IndexOf('/');
        if (slash >= 0 && slash + 1 < entityId.Length)
        {
            return entityId[(slash + 1)..];
        }

        var field = TryReadField(newValuesJson);
        if (field != null)
        {
            return field;
        }

        var changed = ChangedFieldLabels(oldValuesJson, newValuesJson);
        return changed.Count > 0 ? string.Join(", ", changed) : null;
    }

    /// <summary>Reads the "Feld" string of a {"Feld":"…"} payload (else null).</summary>
    private static string? TryReadField(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                && doc.RootElement.TryGetProperty("Feld", out var feld)
                && feld.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                return feld.GetString();
            }
        }
        catch (System.Text.Json.JsonException) { /* not recognised JSON */ }
        return null;
    }

    /// <summary>Diffs two before/after snapshot objects and returns the German
    /// labels of the keys whose value changed (names only, never the values).</summary>
    private static List<string> ChangedFieldLabels(string? oldJson, string? newJson)
    {
        if (string.IsNullOrEmpty(oldJson) || string.IsNullOrEmpty(newJson)) return [];
        try
        {
            using var oldDoc = System.Text.Json.JsonDocument.Parse(oldJson);
            using var newDoc = System.Text.Json.JsonDocument.Parse(newJson);
            if (oldDoc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object
                || newDoc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                return [];
            }

            var labels = new List<string>();
            foreach (var prop in newDoc.RootElement.EnumerateObject())
            {
                var changed = !oldDoc.RootElement.TryGetProperty(prop.Name, out var oldVal)
                    || oldVal.GetRawText() != prop.Value.GetRawText();
                if (changed)
                {
                    labels.Add(FieldLabels.GetValueOrDefault(prop.Name, prop.Name));
                }
            }
            return labels;
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
    }

    /// <summary>Parses the leading "guid" (before any "/") of an EntityId.</summary>
    private static Guid? LeadingStudentId(string entityId)
    {
        var head = entityId.Split('/', 2)[0];
        return Guid.TryParse(head, out var id) ? id : null;
    }

    private static int OrderOf(string key)
    {
        for (var i = 0; i < AuditCategory.All.Count; i++)
        {
            if (AuditCategory.All[i].Key == key) return i;
        }
        return int.MaxValue;
    }
}
