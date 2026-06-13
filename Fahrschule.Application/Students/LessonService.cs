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
                Dauer = request.DurationMinutes,
                Punkte = covered.Count,
            }), cancellationToken: ct);

        return await ReloadDtoAsync(lesson.Id, ct);
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
        DurationMinutes = l.DurationMinutes,
        Note = l.Note,
        CoveredTitles = [.. l.Items
            .Where(i => i.StudentProgressItem != null)
            .Select(i => i.StudentProgressItem!.Title)
            .OrderBy(t => t)],
    };

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
