using System.Globalization;
using System.Text.Json;
using Fahrschule.Application.Audit;
using Fahrschule.Application.Common;
using Fahrschule.Application.LicenseClasses;
using Fahrschule.Contracts.Students;
using Fahrschule.Domain.Entities;
using Fahrschule.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Fahrschule.Application.Payments;

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
public class LessonService(FahrschuleDbContext db, IAuditWriter auditWriter, IPaymentService payments) : ILessonService
{
    public async Task<List<LessonDto>> GetForStudentAsync(Guid studentId, CancellationToken ct = default)
    {
        var lessons = await db.Lessons
            .Include(l => l.LicenseClass)
            .Include(l => l.Items).ThenInclude(i => i.StudentProgressItem)
            .Where(l => l.StudentId == studentId)
            .OrderByDescending(l => l.DateOn).ThenByDescending(l => l.CreatedAtUtc)
            .ToListAsync(ct);

        var money = await MoneyByLessonAsync(lessons.Select(l => l.Id).ToList(), ct);
        return lessons.Select(l => ToDto(l, money)).ToList();
    }

    /// <summary>Paid amount per lesson (plus the receipt number once it is on
    /// one) - so the hours list can show what was paid (KONZEPT 3.6).</summary>
    private async Task<Dictionary<Guid, LessonMoney>> MoneyByLessonAsync(List<Guid> lessonIds, CancellationToken ct)
    {
        var rows = await db.PaymentItems
            .Where(i => i.LessonId != null && lessonIds.Contains(i.LessonId!.Value))
            .Select(i => new
            {
                LessonId = i.LessonId!.Value,
                i.GrossAmount,
                i.VatRatePercent,
                ReceiptNumber = i.Receipt != null ? i.Receipt.Number : null,
            })
            .ToListAsync(ct);

        return rows.ToDictionary(r => r.LessonId,
            r => new LessonMoney(r.GrossAmount, r.VatRatePercent, r.ReceiptNumber));
    }

    /// <summary>What was paid for one lesson.</summary>
    private sealed record LessonMoney(decimal Amount, int VatRatePercent, string? ReceiptNumber);

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

        var wantedCounts = CountsById(request.CountedSessions);
        foreach (var item in covered)
        {
            var countable = StudentProgressRules.IsCountable(item.RequiredCount);
            var countedSessions = CountedSessionsFor(item, wantedCounts);
            lesson.Items.Add(new LessonItem
            {
                LessonId = lesson.Id,
                StudentProgressItemId = item.Id,
                CountedSessions = countedSessions,
            });

            if (countable)
            {
                // Each counted session gets its OWN row - so two Autobahnfahrten
                // driven in one go show up as two counted hours, each removable
                // on its own. 0 = only practised: the link above stays (the topic
                // is recorded), but the counter does not move.
                for (var i = 0; i < countedSessions; i++)
                {
                    db.Set<StudentProgressEntry>().Add(
                        NewEntry(item.Id, lesson.Id, request.DateOn, note, now));
                }
            }
            else
            {
                // A simple point is ticked off on the lesson's date.
                item.IsCompleted = true;
                if (item.CompletedOn is null || item.CompletedOn > request.DateOn)
                {
                    item.CompletedOn = request.DateOn;
                }
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

        // Money paid for this lesson (KONZEPT 3.6). It is stored as a payment
        // item, so the receipt has ONE source for all amounts.
        await payments.SetLessonPaymentAsync(
            lesson, request.LicenseClassId is null ? null : classLabel,
            request.PaidAmount, request.PaidVatRatePercent, actor, ct);
        await db.SaveChangesAsync(ct);

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
            .Include(l => l.Items)
            // The class comes along because the paid amount's description carries
            // it ("Fahrstunde 45 Min. (Klasse B)") - without it the label would
            // silently lose the class when the lesson is corrected.
            .Include(l => l.LicenseClass)
            .FirstOrDefaultAsync(l => l.Id == lessonId && l.StudentId == studentId, ct)
            ?? throw new NotFoundException("Diese Stunde wurde nicht gefunden. Bitte die Liste neu laden.");

        var desiredIds = request.CoveredItemIds.Distinct().ToList();
        var covered = await db.StudentProgressItems
            .Where(p => p.StudentId == studentId && desiredIds.Contains(p.Id))
            .ToListAsync(ct);
        if (covered.Count != desiredIds.Count)
        {
            throw new AppValidationException("Mindestens ein behandelter Punkt wurde nicht gefunden. Bitte die Seite neu laden.");
        }

        var now = DateTime.UtcNow;
        var note = NullIfEmpty(request.Note);
        var wantedCounts = CountsById(request.CountedSessions);
        var coveredById = covered.ToDictionary(p => p.Id);
        var currentByItem = lesson.Items.ToDictionary(li => li.StudentProgressItemId);
        var lessonEntries = await db.Set<StudentProgressEntry>()
            .Where(e => e.LessonId == lessonId)
            .ToListAsync(ct);
        // The counted sessions that survive this edit - only those keep their
        // date/note in sync below (a removed one is on its way out).
        var keptEntries = new List<StudentProgressEntry>();

        // Simple points whose completion may change (recomputed at the end).
        var affected = new HashSet<Guid>();

        // 1) Removed coverage: drop the link + ALL counted sessions it produced.
        foreach (var li in lesson.Items.ToList())
        {
            if (desiredIds.Contains(li.StudentProgressItemId)) continue;
            lesson.Items.Remove(li);
            db.Set<LessonItem>().Remove(li);
            foreach (var entry in lessonEntries.Where(e => e.StudentProgressItemId == li.StudentProgressItemId))
            {
                db.Set<StudentProgressEntry>().Remove(entry);
            }
            affected.Add(li.StudentProgressItemId);
        }

        // 2) Added or changed coverage - including a changed NUMBER ("counted as
        //    2 instead of 1"), which adds or removes counted sessions.
        foreach (var id in desiredIds)
        {
            var item = coveredById[id];
            var wanted = CountedSessionsFor(item, wantedCounts);

            if (currentByItem.TryGetValue(id, out var li))
            {
                li.CountedSessions = wanted;
            }
            else
            {
                // Newly covered.
                lesson.Items.Add(new LessonItem
                {
                    LessonId = lessonId, StudentProgressItemId = id, CountedSessions = wanted,
                });
                affected.Add(id);
            }

            // Bring the counted sessions in line with the wanted number: add the
            // missing ones, remove the surplus (the youngest first, so the oldest
            // record of the drive survives).
            var existing = lessonEntries
                .Where(e => e.StudentProgressItemId == id)
                .OrderBy(e => e.CreatedAtUtc).ToList();
            for (var i = existing.Count; i < wanted; i++)
            {
                db.Set<StudentProgressEntry>().Add(NewEntry(id, lessonId, request.DateOn, note, now));
            }
            for (var i = wanted; i < existing.Count; i++)
            {
                db.Set<StudentProgressEntry>().Remove(existing[i]);
            }
            keptEntries.AddRange(existing.Take(wanted));
        }

        // 3) The lesson's own fields; keep its counted sessions' date/note in sync.
        lesson.DateOn = request.DateOn;
        lesson.StartTime = startTime;
        lesson.DurationMinutes = request.DurationMinutes;
        lesson.Note = note;
        foreach (var e in keptEntries) { e.PerformedOn = request.DateOn; e.Note = note; }

        await db.SaveChangesAsync(ct);

        // 3b) The paid amount (KONZEPT 3.6). If it is already on a receipt, the
        // service refuses a CHANGED amount - an issued receipt must not move.
        await payments.SetLessonPaymentAsync(
            lesson, lesson.LicenseClass?.Code, request.PaidAmount, request.PaidVatRatePercent, actor, ct);
        await db.SaveChangesAsync(ct);

        // 4) Recompute the simple points the change touched.
        await ProgressCoupling.RecomputeSimpleAsync(db, affected.ToList(), now, ct);
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(actor.UserId, actor.UserName, "Stunde geändert",
            "Ausbildungsstunde", studentId.ToString(),
            newValuesJson: JsonSerializer.Serialize(new
            {
                Datum = request.DateOn.ToString("dd.MM.yyyy"),
                Start = startTime.ToString("HH\\:mm"),
                Dauer = request.DurationMinutes,
                Punkte = desiredIds.Count,
            }), cancellationToken: ct);

        return await ReloadDtoAsync(lesson.Id, ct);
    }

    private static StudentProgressEntry NewEntry(Guid itemId, Guid lessonId, DateOnly date, string? note, DateTime now)
        => new()
        {
            Id = Guid.NewGuid(),
            StudentProgressItemId = itemId,
            LessonId = lessonId,
            PerformedOn = date,
            Note = note,
            CreatedAtUtc = now,
        };

    /// <summary>The requested numbers as a lookup. A point named twice keeps its
    /// LAST number - the request is a wish list, not a place to fail on.</summary>
    private static Dictionary<Guid, int> CountsById(LessonItemCountRequest[] requested)
    {
        var counts = new Dictionary<Guid, int>();
        foreach (var entry in requested) counts[entry.ItemId] = entry.Count;
        return counts;
    }

    /// <summary>
    /// How often a covered point counts in this lesson: for a COUNTABLE point the
    /// requested number (not mentioned = 1, the normal full session), for a simple
    /// point always 0 - a theory topic is "done", there is nothing to count.
    /// </summary>
    private static int CountedSessionsFor(StudentProgressItem item, IReadOnlyDictionary<Guid, int> requested)
    {
        if (!StudentProgressRules.IsCountable(item.RequiredCount)) return 0;
        if (!requested.TryGetValue(item.Id, out var count)) return 1;
        if (count < 0 || count > StudentProgressRules.MaxCountedSessionsPerLesson)
        {
            throw new AppValidationException(
                $"Bitte bei „{item.Title}“ eine Anzahl zwischen 0 und " +
                $"{StudentProgressRules.MaxCountedSessionsPerLesson} eintragen.");
        }
        return count;
    }

    public async Task DeleteAsync(Guid studentId, Guid lessonId, Actor actor, CancellationToken ct = default)
    {
        var lesson = await db.Lessons
            .Include(l => l.LicenseClass)
            .Include(l => l.Items)
            .FirstOrDefaultAsync(l => l.Id == lessonId && l.StudentId == studentId, ct)
            ?? throw new NotFoundException("Diese Stunde wurde nicht gefunden. Bitte die Liste neu laden.");

        // Money first: if the paid amount is already on a receipt, the lesson
        // must stay - otherwise a handed-out document would lose its basis.
        await payments.EnsureLessonMoneyEditableAsync(lessonId, ct);

        var coveredItemIds = lesson.Items.Select(i => i.StudentProgressItemId).ToList();

        // Soft-delete (project rule 7): the lesson vanishes from the hours list
        // but stays recoverable. Its counted sessions are removed so the counters
        // drop, and the simple points it covered are recomputed below - a point
        // only stays "done" if another lesson (or a manual mark) covers it.
        var now = DateTime.UtcNow;
        var entries = await db.Set<StudentProgressEntry>()
            .Where(e => e.LessonId == lessonId).ToListAsync(ct);
        db.Set<StudentProgressEntry>().RemoveRange(entries);

        lesson.IsDeleted = true;
        lesson.DeletedAtUtc = now;
        lesson.DeletedByUserId = actor.UserId;

        // The not-yet-receipted paid amount goes with it (soft-delete as well).
        await payments.SetLessonPaymentAsync(lesson, null, null, null, actor, ct);
        await db.SaveChangesAsync(ct);

        await ProgressCoupling.RecomputeSimpleAsync(db, coveredItemIds, now, ct);
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
        var money = await MoneyByLessonAsync([lessonId], ct);
        return ToDto(lesson, money);
    }

    private async Task<string> ClassLabelAsync(Guid? licenseClassId, CancellationToken ct)
    {
        if (licenseClassId is null) return "Grundstoff";
        var code = await db.LicenseClasses
            .Where(c => c.Id == licenseClassId)
            .Select(c => c.Code).FirstOrDefaultAsync(ct);
        return code ?? "Klasse";
    }

    private static LessonDto ToDto(Lesson l, Dictionary<Guid, LessonMoney>? money = null) => new()
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
            .Select(CoveredTitle)
            .OrderBy(t => t)],
        Covered = [.. l.Items
            .Where(i => i.StudentProgressItem != null)
            .Select(i => new LessonCoverDto
            {
                ItemId = i.StudentProgressItemId,
                Title = i.StudentProgressItem!.Title,
                IsCountable = StudentProgressRules.IsCountable(i.StudentProgressItem!.RequiredCount),
                CountedSessions = i.CountedSessions,
            })
            .OrderBy(c => c.Title)],
        PaidAmount = Money(money, l.Id)?.Amount,
        PaidVatRatePercent = Money(money, l.Id)?.VatRatePercent,
        PaidReceiptNumber = Money(money, l.Id)?.ReceiptNumber,
    };

    /// <summary>The covered point as it reads in the hours list AND on the
    /// printed Ausbildungsnachweis. Counted more than once, the number belongs
    /// next to it ("Autobahnfahrt (2×)") - otherwise one line would silently
    /// stand for two driven sessions.</summary>
    private static string CoveredTitle(LessonItem item)
        => item.CountedSessions > 1
            ? $"{item.StudentProgressItem!.Title} ({item.CountedSessions}×)"
            : item.StudentProgressItem!.Title;

    private static LessonMoney? Money(Dictionary<Guid, LessonMoney>? money, Guid lessonId)
        => money is not null && money.TryGetValue(lessonId, out var m) ? m : null;

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Parses a "HH:mm" time string (same convention as the calendar).</summary>
    private static bool TryParseTime(string? value, out TimeOnly time)
        => TimeOnly.TryParse(value, CultureInfo.InvariantCulture, out time);
}
