using Fahrschule.Contracts.Admin;
using Fahrschule.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fahrschule.Application.Audit;

public interface IAuditQueryService
{
    Task<AuditListResultDto> GetListAsync(string? search, int page, int pageSize, CancellationToken ct = default);
}

/// <summary>
/// Read access to the audit log for the admin panel (KONZEPT 3.7): "who changed
/// what, when". Filterable by a free-text term (user, action, record) and
/// paginated; newest first. The log itself is append-only (see AuditWriter) -
/// this service never modifies it.
/// </summary>
public class AuditQueryService(FahrschuleDbContext db) : IAuditQueryService
{
    private const int MaxPageSize = 100;

    public async Task<AuditListResultDto> GetListAsync(string? search, int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize <= 0 ? 20 : pageSize, 1, MaxPageSize);

        var query = db.AuditLogs.AsQueryable();

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

        var items = await query
            .OrderByDescending(a => a.TimestampUtc).ThenByDescending(a => a.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new AuditLogDto
            {
                Id = a.Id,
                TimestampUtc = a.TimestampUtc,
                UserName = a.UserName,
                Action = a.Action,
                EntityType = a.EntityType,
                EntityId = a.EntityId,
                OldValuesJson = a.OldValuesJson,
                NewValuesJson = a.NewValuesJson,
            })
            .ToListAsync(ct);

        return new AuditListResultDto { Items = items, Total = total, Page = page, PageSize = pageSize };
    }
}
