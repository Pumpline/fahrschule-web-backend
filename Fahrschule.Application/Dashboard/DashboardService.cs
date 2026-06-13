using Fahrschule.Application.Audit;
using Fahrschule.Application.Settings;
using Fahrschule.Application.Students;
using Fahrschule.Contracts.Dashboard;
using Fahrschule.Domain.Entities;
using Fahrschule.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fahrschule.Application.Dashboard;

public interface IDashboardService
{
    Task<DashboardDto> GetAsync(CancellationToken ct = default);
}

/// <summary>
/// The start-page dashboard (KONZEPT 3.1/3a): "Bald fällig" (documents expiring
/// within the configurable reminder window, so the office can inform the
/// student) and "Letzte Änderungen" (the latest audit entries). It only reads
/// and aggregates data that already exists elsewhere.
/// </summary>
public class DashboardService(
    FahrschuleDbContext db,
    ISettingsService settingsService,
    IAuditQueryService auditQuery) : IDashboardService
{
    private const int MaxUpcoming = 50;
    private const int RecentChanges = 8;

    public async Task<DashboardDto> GetAsync(CancellationToken ct = default)
    {
        var reminderDays = (await settingsService.GetAsync(ct)).DocumentExpiryReminderDays;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var threshold = today.AddDays(reminderDays);

        // Documents with an expiry date within the window (or already overdue).
        // The global query filter already excludes deleted students/catalogue items.
        var docs = await db.Set<DocumentChecklistItem>()
            .Where(d => d.ExpiresOn != null && d.ExpiresOn <= threshold)
            .Include(d => d.Student)
            .Include(d => d.DocumentCatalogItem)
            .OrderBy(d => d.ExpiresOn)
            .Take(MaxUpcoming)
            .ToListAsync(ct);

        var upcoming = docs
            .Where(d => d.Student != null && d.DocumentCatalogItem != null)
            .Select(d => new UpcomingDocumentDto
            {
                StudentId = d.StudentId,
                StudentName = $"{d.Student!.FirstName} {d.Student!.LastName}".Trim(),
                DocumentName = d.DocumentCatalogItem!.Name,
                ExpiresOn = d.ExpiresOn!.Value,
                DaysUntilExpiry = DocumentChecklistRules.DaysUntilExpiry(d.ExpiresOn, today),
            })
            .ToList();

        var recent = (await auditQuery.GetListAsync(search: null, page: 1, pageSize: RecentChanges, ct)).Items;

        return new DashboardDto { UpcomingDocuments = upcoming, RecentChanges = recent };
    }
}
