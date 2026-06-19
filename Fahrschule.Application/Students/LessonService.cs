using System.Globalization;
using System.Text.Json;
using Fahrschule.Application.Audit;
using Fahrschule.Application.Common;
using Fahrschule.Application.LicenseClasses;
using Fahrschule.Contracts.Students;
using Fahrschule.Domain.Entities;
using Fahrschule.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fahrschule.Application.Students;

public interface ILessonService
{
    Task<List<LessonDto>> GetForStudentAsync(Guid studentId, CancellationToken ct = default);
    Task<LessonDto> CreateAsync(Guid studentId, CreateLessonRequest request, Actor actor, CancellationToken ct = default);
    Task<LessonDto> UpdateAsync(Guid studentId, Guid lessonId, UpdateLessonRequest request, Actor actor, CancellationToken ct = default);
    Task DeleteAsync(Guid studentId, Guid lessonId, Actor actor, CancellationToken ct = default);
}

/// <summary>
/// Business logic for entering teaching units (KONZEPT 3.3). A lesson is the
/// single place where training is recorded: theory or practice, for a class (or
/// shared "Grundstoff"), date, duration, and the covered points. Saving it
/// applies the effect to the student's progress - simple points are ticked off,
/// countable ones get a counted session - and links the points to the lesson so
/// it can later appear on the Ausbildungsnachweis.
/// </summary>
public class LessonService(FahrschuleDbContext db, IAuditWriter auditWriter) : ILessonService
{
    public async Task<List<LessonDto>> GetForStudentAsync(Guid studentId, CancellationToken ct = default)
    {
        var lessons = await db.Lessons
            .Include(l => l.LicenseClass)
            .Include(l => l.Items).ThenInclude(i => i.StudentProgressItem)
            .Where(l => l.StudentId == studentId)
            .OrderByDescending(l => l.DateOn).ThenByDescending(l => l.CreatedAtUtc)
            .ToListAsync(ct);

        return lessons.Select(ToDto).ToList();
    }

    public async Task<LessonDto> CreateAsync(
        Guid studentId, CreateLessonRequest request, Actor actor, CancellationToken ct = default)
    {
        if (!Enum.TryParse<LessonType>(request.Type, out var type))
        {
            throw new AppValidationException("Bitte Theorie oder Praxis wählen.");
        }
        if (request.DurationMinutes <= 0)
        {
            throw new AppValidationException("Bitte eine Dauer für die Stunde wählen.");
        }
        if (!TryParseTime(request.StartTime, out var startTime))
        {
            throw new AppValidationException("Bitte eine gültige Startzeit (z. B. 18:00) eintragen.");
        }

        var student = await db.Students
            .Include(s => s.LicenseClasses)
            .FirstOrDefaultAsync(s => s.Id == studentId, ct)
            ?? throw new NotFoundException("Dieser Schüler wurde nicht gefunden. Bitte die Liste neu laden.");

        // A chosen class must belong to the student (null = shared "Grundstoff").
        if (request.LicenseClassId is { } classId &&
            student.LicenseClasses.All(lc => lc.LicenseClassId != classId))
        {
            throw new AppValidationException("Diese Klasse ist beim Schüler nicht eingetragen. Bitte die Seite neu laden.");
        }

        var ids = request.CoveredItemIds.Distinct().ToList();
        var covered = await db.StudentProgressItems
            .Where(p => p.StudentId == studentId && ids.Contains(p.Id))
            .ToListAsync(ct);
        if (covered.Count != ids.Count)
        {
            throw new AppValidationException("Mindestens ein behandelter Punkt wurde nicht gefunden. Bitte die Seite neu laden.");
        }

        var now = DateTime.UtcNow;
        var note = NullIfEmpty(request.Note);
        var lesson = new Lesson
        {
            Id = Guid.NewGuid(),
            StudentId = studentId,
            Type = type,
            LicenseClassId = request.LicenseClassId,
            DateOn = request.DateOn,
            StartTime = startTime,
            DurationMinutes = request.DurationMinutes,
            Note = note,
            CreatedAtUtc = now,
        };
        db.Lessons.Add(lesson);

        foreach (var item in covered)
        {
            lesson.Items.Add(new LessonItem { LessonId = lesson.Id, StudentProgressItemId = item.Id });

            if (StudentProgressRules.IsCountable(item.RequiredCount))
            {
                // A countable point gets one counted session for this lesson.
                db.Set<StudentProgressEntry>().Add(new StudentProgressEntry
                {
                    Id = Guid.NewGuid(),
                    StudentProgressItemId = item.Id,
                    PerformedOn = request.DateOn,
                    Note = note,
                    CreatedAtUtc = now,
                });
            }
            else if (!item.IsCompleted)
            {
                // A simple point is ticked off on the lesson's date.
                item.IsCompleted = true;
                item.CompletedOn = request.DateOn;
                if (note is not null) item.Note = note;
            }
            item.UpdatedAtUtc = now;
        }

        // If this lesson was carried out for a planned appointment, mark that
        // appointment "durchgeführt" and link it (KONZEPT 3.5). Only when it
        // belongs to the same student and isn't already linked.
        if (request.CalendarEventId is { } eventId)
        {
            var calendarEvent = await db.CalendarEvents.FirstOrDefaultAsync(e => e.Id == eventId, ct);
            if (calendarEvent is not null && calendarEvent.StudentId == studentId && calendarEvent.LessonId is null)
            {
                calendarEvent.LessonId = lesson.Id;
            }
        }

        await db.SaveChangesAsync(ct);

        var classLabel = await ClassLabelAsync(request.LicenseClassId, ct);
        await auditWriter.WriteAsync(actor.UserId, actor.UserName, "Stunde eingetragen",
            "Ausbildungsstunde", studentId.ToString(),
            newValuesJson: JsonSerializer.Serialize(new
            {
                Typ = type.ToString(),
                Klasse = classLabel,
                Datum = request.DateOn.ToString("dd.MM.yyyy"),
                Start = startTime.ToString("HH\\:mm"),
                Dauer = request.DurationMinutes,
                Punkte = covered.Count,
            }), cancellationToken: ct);

        return await ReloadDtoAsync(lesson.Id, ct);
    }

    public async Task<LessonDto> UpdateAsync(
        Guid studentId, Guid lessonId, UpdateLessonRequest request, Actor actor, CancellationToken ct = default)
    {
        if (request.DurationMinutes <= 0)
        {
            throw new AppValidationException("Bitte eine Dauer für die Stunde wählen.");
        }
        if (!TryParseTime(request.StartTime, out var startTime))
        {
            throw new AppValidationException("Bitte eine gültige Startzeit (z. B. 18:00) eintragen.");
        }

        var lesson = await db.Lessons
            .FirstOrDefaultAsync(l => l.Id == lessonId && l.StudentId == studentId, ct)
            ?? throw new NotFoundException("Diese Stunde wurde nicht gefunden. Bitte die Liste neu laden.");

        // Only the lesson's own fields are correctable here (the type/class and
        // covered points define the progress linkage - changing those means
        // delete + re-enter). This keeps the progress effect consistent.
        lesson.DateOn = request.DateOn;
        lesson.StartTime = startTime;
        lesson.DurationMinutes = request.DurationMinutes;
        lesson.Note = NullIfEmpty(request.Note);

        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(actor.UserId, actor.UserName, "Stunde geändert",
            "Ausbildungsstunde", studentId.ToString(),
            newValuesJson: JsonSerializer.Serialize(new
            {
                Datum = request.DateOn.ToString("dd.MM.yyyy"),
                Start = startTime.ToString("HH\\:mm"),
                Dauer = request.DurationMinutes,
            }), cancellationToken: ct);

        return await ReloadDtoAsync(lesson.Id, ct);
    }

    public async Task DeleteAsync(Guid studentId, Guid lessonId, Actor actor, CancellationToken ct = default)
    {
        var lesson = await db.Lessons
            .Include(l => l.LicenseClass)
            .FirstOrDefaultAsync(l => l.Id == lessonId && l.StudentId == studentId, ct)
            ?? throw new NotFoundException("Diese Stunde wurde nicht gefunden. Bitte die Liste neu laden.");

        // Soft-delete (project rule 7): the lesson vanishes from the hours list
        // but stays recoverable. The already-ticked points/counters in the
        // progress are deliberately left untouched (they are edited separately).
        lesson.IsDeleted = true;
        lesson.DeletedAtUtc = DateTime.UtcNow;
        lesson.DeletedByUserId = actor.UserId;
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(actor.UserId, actor.UserName, "Stunde gelöscht",
            "Ausbildungsstunde", studentId.ToString(),
            oldValuesJson: JsonSerializer.Serialize(new
            {
                Typ = lesson.Type.ToString(),
                Klasse = lesson.LicenseClass?.Code ?? "Grundstoff",
                Datum = lesson.DateOn.ToString("dd.MM.yyyy"),
                Start = lesson.StartTime.ToString("HH\\:mm"),
            }), cancellationToken: ct);
    }

    private async Task<LessonDto> ReloadDtoAsync(Guid lessonId, CancellationToken ct)
    {
        var lesson = await db.Lessons
            .Include(l => l.LicenseClass)
            .Include(l => l.Items).ThenInclude(i => i.StudentProgressItem)
            .FirstAsync(l => l.Id == lessonId, ct);
        return ToDto(lesson);
    }

    private async Task<string> ClassLabelAsync(Guid? licenseClassId, CancellationToken ct)
    {
        if (licenseClassId is null) return "Grundstoff";
        var code = await db.LicenseClasses
            .Where(c => c.Id == licenseClassId)
            .Select(c => c.Code).FirstOrDefaultAsync(ct);
        return code ?? "Klasse";
    }

    private static LessonDto ToDto(Lesson l) => new()
    {
        Id = l.Id,
        Type = l.Type.ToString(),
        LicenseClassId = l.LicenseClassId,
        ClassLabel = l.LicenseClass?.Code ?? "Grundstoff",
        DateOn = l.DateOn,
        StartTime = l.StartTime.ToString("HH\\:mm"),
        DurationMinutes = l.DurationMinutes,
        Note = l.Note,
        CoveredTitles = [.. l.Items
            .Where(i => i.StudentProgressItem != null)
            .Select(i => i.StudentProgressItem!.Title)
            .OrderBy(t => t)],
    };

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Parses a "HH:mm" time string (same convention as the calendar).</summary>
    private static bool TryParseTime(string? value, out TimeOnly time)
        => TimeOnly.TryParse(value, CultureInfo.InvariantCulture, out time);
}
