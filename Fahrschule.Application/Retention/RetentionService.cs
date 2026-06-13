using System.Text.Json;
using Fahrschule.Application.Audit;
using Fahrschule.Application.LicenseClasses;
using Fahrschule.Application.Settings;
using Fahrschule.Application.Students;
using Fahrschule.Contracts.Admin;
using Fahrschule.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fahrschule.Application.Retention;

public interface IRetentionService
{
    /// <summary>
    /// Lists every student marked for deletion together with the date the data
    /// will be removed permanently, and how many are already due.
    /// </summary>
    Task<RetentionStatusDto> GetStatusAsync(CancellationToken ct = default);

    /// <summary>
    /// Permanently removes every soft-deleted student whose retention deadline
    /// has passed (KONZEPT 3.7 / rule 7). Pass <paramref name="actor"/> = null
    /// for the automatic background run; a non-null actor records who triggered
    /// a manual run. Returns how many students were removed.
    /// </summary>
    Task<RetentionRunResultDto> RunAsync(Actor? actor = null, CancellationToken ct = default);
}

/// <summary>
/// The retention job (KONZEPT rule 7: "echtes Entfernen ausschließlich durch
/// den Aufbewahrungs-Job nach Fristende"). Soft delete only flags a student;
/// this service is the single place that actually erases the data once the
/// configured retention period has elapsed.
///
/// The period is a setting, not a constant (rule 3), so the owner can match it
/// to the legally required retention without new code. The run is idempotent
/// and safe to call repeatedly (the background service calls it daily, and the
/// admin can trigger it manually).
/// </summary>
public class RetentionService(
    FahrschuleDbContext db,
    ISettingsService settings,
    IStudentService students,
    IAuditWriter auditWriter) : IRetentionService
{
    public async Task<RetentionStatusDto> GetStatusAsync(CancellationToken ct = default)
    {
        var days = (await settings.GetAsync(ct)).RetentionStudentDays;
        var now = DateTime.UtcNow;

        // Reuse the existing "marked for deletion" list and enrich each entry
        // with its deadline so the admin panel can show when data disappears.
        var marked = await students.GetDeletedAsync(ct);

        var enriched = marked.Select(s =>
        {
            var dueAt = s.DeletedAtUtc?.AddDays(days);
            return new RetentionMarkedStudentDto
            {
                Id = s.Id,
                FullName = s.FullName,
                ClassCodes = s.ClassCodes,
                DeletedAtUtc = s.DeletedAtUtc,
                DueAtUtc = dueAt,
                IsDue = dueAt is { } due && due <= now,
            };
        }).ToList();

        return new RetentionStatusDto
        {
            RetentionDays = days,
            DueCount = enriched.Count(s => s.IsDue),
            Marked = enriched,
        };
    }

    public async Task<RetentionRunResultDto> RunAsync(Actor? actor = null, CancellationToken ct = default)
    {
        var days = (await settings.GetAsync(ct)).RetentionStudentDays;
        // Everything marked on or before this moment is past its deadline.
        var cutoff = DateTime.UtcNow.AddDays(-days);

        // Bypass the soft-delete filter to reach the flagged rows.
        var due = await db.Students.IgnoreQueryFilters()
            .Where(s => s.IsDeleted && s.DeletedAtUtc != null && s.DeletedAtUtc <= cutoff)
            .ToListAsync(ct);

        var deletedCount = 0;
        foreach (var student in due)
        {
            // Capture for the audit entry before the row is gone.
            var id = student.Id;
            var name = $"{student.FirstName} {student.LastName}".Trim();

            // Calendar events reference the student with DeleteBehavior.Restrict
            // (a soft-deleted student normally keeps them), so the database would
            // refuse the delete. Remove the personal appointments first; the
            // remaining dependents (registrations, progress, lessons, exams) are
            // configured as cascade and go automatically.
            var events = await db.CalendarEvents.Where(e => e.StudentId == id).ToListAsync(ct);
            db.CalendarEvents.RemoveRange(events);

            db.Students.Remove(student);
            await db.SaveChangesAsync(ct);

            // Audit (rule 1): record the permanent erasure. We keep only the name
            // and the original deletion date - enough to prove the frist was kept,
            // without re-storing the full personal record we just erased.
            var snapshot = JsonSerializer.Serialize(new
            {
                Name = name,
                VorgemerktAm = student.DeletedAtUtc,
                Aufbewahrungsfrist = $"{days} Tage",
            });
            await auditWriter.WriteAsync(
                actor?.UserId, actor?.UserName ?? "System (Aufbewahrung)",
                "Endgültig gelöscht", "Schüler", id.ToString(),
                oldValuesJson: snapshot, cancellationToken: ct);

            deletedCount++;
        }

        return new RetentionRunResultDto { DeletedCount = deletedCount };
    }
}
