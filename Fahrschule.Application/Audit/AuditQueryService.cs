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
