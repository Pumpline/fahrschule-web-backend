using System.Text.Json;
using Fahrschule.Application.Audit;
using Fahrschule.Application.LicenseClasses;
using Fahrschule.Application.Settings;
using Fahrschule.Contracts.Admin;
using Fahrschule.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fahrschule.Application.Retention;

public interface IRetentionService
{
    /// <summary>
    /// Lists the students whose legal retention deadline has been reached (and
    /// the configured period), so the admin panel can show what will be removed.
    /// </summary>
    Task<RetentionStatusDto> GetStatusAsync(CancellationToken ct = default);

    /// <summary>
    /// Permanently removes every student whose retention deadline has passed
    /// (§ 31 Abs. 3 FahrlG: delete without delay after the frist). Pass
    /// <paramref name="actor"/> = null for the automatic background run; a
    /// non-null actor records who triggered a manual run. Returns how many
    /// students were removed.
    /// </summary>
    Task<RetentionRunResultDto> RunAsync(Actor? actor = null, CancellationToken ct = default);
}

/// <summary>
/// The retention job (KONZEPT rule 7 / § 31 Abs. 3 FahrlG). Student training
/// records must be kept for a number of years after the end of the training
/// year, then deleted without delay. This service is the single place that
/// actually erases the data; the deadline is computed per student from the last
/// instruction activity (see <see cref="StudentRetentionRules"/>) and the
/// editable retention period (rule 3). It runs over ALL students - deletion is
/// driven by the legal deadline, not by a manual "marked for deletion" flag.
/// The run is idempotent and safe to call repeatedly (daily background job +
/// manual trigger).
/// </summary>
public class RetentionService(
    FahrschuleDbContext db,
    ISettingsService settings,
    IAuditWriter auditWriter) : IRetentionService
{
    public async Task<RetentionStatusDto> GetStatusAsync(CancellationToken ct = default)
    {
        var years = (await settings.GetAsync(ct)).RetentionStudentYears;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var (lastLesson, lastExam) = await LoadActivityAsync(ct);

        // Include classes for display; bypass the soft-delete filter so hidden
        // (soft-deleted) students are considered for the legal deadline too.
        var students = await db.Students.IgnoreQueryFilters()
            .Include(s => s.LicenseClasses).ThenInclude(lc => lc.LicenseClass)
            .ToListAsync(ct);

        var due = students
            .Select(s =>
            {
                var end = StudentRetentionRules.TrainingEndDate(
                    DateOnly.FromDateTime(s.CreatedAtUtc), Lookup(lastLesson, s.Id), Lookup(lastExam, s.Id));
                return (Student: s, TrainingEnd: end);
            })
            .Where(x => StudentRetentionRules.IsDue(today, x.TrainingEnd, years))
            .Select(x => new RetentionDueStudentDto
            {
                Id = x.Student.Id,
                FullName = $"{x.Student.FirstName} {x.Student.LastName}".Trim(),
                ClassCodes = [.. x.Student.LicenseClasses.Where(lc => lc.LicenseClass != null)
                    .Select(lc => lc.LicenseClass!.Code).OrderBy(c => c)],
                TrainingEndDate = x.TrainingEnd,
                DueDate = StudentRetentionRules.DeletionDueDate(x.TrainingEnd, years),
            })
            .OrderBy(d => d.DueDate).ThenBy(d => d.FullName)
            .ToList();

        return new RetentionStatusDto
        {
            RetentionYears = years,
            DueCount = due.Count,
            Due = due,
        };
    }

    public async Task<RetentionRunResultDto> RunAsync(Actor? actor = null, CancellationToken ct = default)
    {
        var years = (await settings.GetAsync(ct)).RetentionStudentYears;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var (lastLesson, lastExam) = await LoadActivityAsync(ct);

        // Tracked entities so we can delete them; bypass the soft-delete filter.
        var students = await db.Students.IgnoreQueryFilters().ToListAsync(ct);

        var deletedCount = 0;
        foreach (var student in students)
        {
            var trainingEnd = StudentRetentionRules.TrainingEndDate(
                DateOnly.FromDateTime(student.CreatedAtUtc),
                Lookup(lastLesson, student.Id), Lookup(lastExam, student.Id));

            if (!StudentRetentionRules.IsDue(today, trainingEnd, years))
            {
                continue;
            }

            var id = student.Id;
            var name = $"{student.FirstName} {student.LastName}".Trim();

            // Calendar events reference the student with DeleteBehavior.Restrict,
            // so the database would refuse the delete. Remove the personal
            // appointments first; the remaining dependents (registrations,
            // progress, lessons, exams) are cascade and go automatically.
            var events = await db.CalendarEvents.Where(e => e.StudentId == id).ToListAsync(ct);
            db.CalendarEvents.RemoveRange(events);

            // Reminders linked to the student also use Restrict - remove them too.
            var reminders = await db.Reminders.Where(r => r.StudentId == id).ToListAsync(ct);
            db.Reminders.RemoveRange(reminders);

            db.Students.Remove(student);
            await db.SaveChangesAsync(ct);

            // Audit (rule 1): record the erasure - but sparingly, only name +
            // the dates that prove the frist was kept, not the data we just erased.
            var snapshot = JsonSerializer.Serialize(new
            {
                Name = name,
                Ausbildungsende = trainingEnd,
                Aufbewahrungsfrist = $"{years} Jahre",
            });
            await auditWriter.WriteAsync(
                actor?.UserId, actor?.UserName ?? "System (Aufbewahrung)",
                "Endgültig gelöscht", "Schüler", id.ToString(),
                oldValuesJson: snapshot, cancellationToken: ct);

            deletedCount++;
        }

        return new RetentionRunResultDto { DeletedCount = deletedCount };
    }

    /// <summary>
    /// Last lesson and last exam date per student. Both queries bypass the
    /// soft-delete filter, otherwise a hidden student's activity would be missed
    /// and the deadline would wrongly fall back to the registration date.
    /// </summary>
    private async Task<(Dictionary<Guid, DateOnly> Lessons, Dictionary<Guid, DateOnly> Exams)> LoadActivityAsync(CancellationToken ct)
    {
        var lessons = await db.Lessons.IgnoreQueryFilters()
            .GroupBy(l => l.StudentId)
            .Select(g => new { Id = g.Key, Last = g.Max(x => x.DateOn) })
            .ToDictionaryAsync(x => x.Id, x => x.Last, ct);

        var exams = await db.Exams.IgnoreQueryFilters()
            .GroupBy(e => e.StudentId)
            .Select(g => new { Id = g.Key, Last = g.Max(x => x.DateOn) })
            .ToDictionaryAsync(x => x.Id, x => x.Last, ct);

        return (lessons, exams);
    }

    private static DateOnly? Lookup(Dictionary<Guid, DateOnly> dates, Guid id)
        => dates.TryGetValue(id, out var value) ? value : null;
}
