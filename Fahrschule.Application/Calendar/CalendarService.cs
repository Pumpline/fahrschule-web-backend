using System.Globalization;
using System.Text.Json;
using Fahrschule.Application.Audit;
using Fahrschule.Application.Common;
using Fahrschule.Application.LicenseClasses;
using Fahrschule.Contracts.Calendar;
using Fahrschule.Domain.Entities;
using Fahrschule.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fahrschule.Application.Calendar;

public interface ICalendarService
{
    Task<List<CalendarEventDto>> GetForMonthAsync(int year, int month, CancellationToken ct = default);
    Task<CalendarEventDto> CreateAsync(SaveCalendarEventRequest request, Actor actor, CancellationToken ct = default);
    Task<CalendarEventDto> UpdateAsync(Guid id, SaveCalendarEventRequest request, Actor actor, CancellationToken ct = default);
    Task DeleteAsync(Guid id, Actor actor, CancellationToken ct = default);
}

/// <summary>
/// Business logic for the calendar (KONZEPT 3.5). One Fahrlehrer for now, so the
/// calendar is global; double bookings are prevented by an overlap check
/// (CalendarRules). Appointments may optionally link to a student. Every change
/// is audited.
/// </summary>
public class CalendarService(FahrschuleDbContext db, IAuditWriter auditWriter) : ICalendarService
{
    public async Task<List<CalendarEventDto>> GetForMonthAsync(int year, int month, CancellationToken ct = default)
    {
        if (month is < 1 or > 12)
        {
            throw new AppValidationException("Ungültiger Monat.");
        }

        var first = new DateOnly(year, month, 1);
        var last = first.AddMonths(1).AddDays(-1);

        var events = await db.CalendarEvents
            .Include(e => e.Student)
            .Where(e => e.DateOn >= first && e.DateOn <= last)
            .OrderBy(e => e.DateOn).ThenBy(e => e.StartTime)
            .ToListAsync(ct);

        return events.Select(ToDto).ToList();
    }

    public async Task<CalendarEventDto> CreateAsync(SaveCalendarEventRequest request, Actor actor, CancellationToken ct = default)
    {
        var (kind, start, end, customTitle) = await ValidateAsync(request, eventId: null, ct);

        var ev = new CalendarEvent
        {
            Id = Guid.NewGuid(),
            DateOn = request.DateOn,
            StartTime = start,
            EndTime = end,
            Kind = kind,
            CustomTitle = customTitle,
            StudentId = request.StudentId,
            Note = NullIfEmpty(request.Note),
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.CalendarEvents.Add(ev);
        await db.SaveChangesAsync(ct);

        await WriteAuditAsync(actor, "Termin angelegt", ev, ct);
        return await ReloadDtoAsync(ev.Id, ct);
    }

    public async Task<CalendarEventDto> UpdateAsync(Guid id, SaveCalendarEventRequest request, Actor actor, CancellationToken ct = default)
    {
        var ev = await db.CalendarEvents.FirstOrDefaultAsync(e => e.Id == id, ct)
            ?? throw new NotFoundException("Dieser Termin wurde nicht gefunden. Bitte den Kalender neu laden.");

        var (kind, start, end, customTitle) = await ValidateAsync(request, eventId: id, ct);

        ev.DateOn = request.DateOn;
        ev.StartTime = start;
        ev.EndTime = end;
        ev.Kind = kind;
        ev.CustomTitle = customTitle;
        ev.StudentId = request.StudentId;
        ev.Note = NullIfEmpty(request.Note);
        await db.SaveChangesAsync(ct);

        await WriteAuditAsync(actor, "Termin geändert", ev, ct);
        return await ReloadDtoAsync(ev.Id, ct);
    }

    public async Task DeleteAsync(Guid id, Actor actor, CancellationToken ct = default)
    {
        var ev = await db.CalendarEvents.FirstOrDefaultAsync(e => e.Id == id, ct)
            ?? throw new NotFoundException("Dieser Termin wurde nicht gefunden. Vielleicht wurde er bereits gelöscht.");

        db.CalendarEvents.Remove(ev);
        await db.SaveChangesAsync(ct);

        await WriteAuditAsync(actor, "Termin gelöscht", ev, ct);
    }

    // --- validation + conflict check ---

    private async Task<(CalendarEventKind Kind, TimeOnly Start, TimeOnly? End, string? CustomTitle)>
        ValidateAsync(SaveCalendarEventRequest request, Guid? eventId, CancellationToken ct)
    {
        if (!Enum.TryParse<CalendarEventKind>(request.Kind, out var kind))
        {
            throw new AppValidationException("Bitte eine Art für den Termin wählen.");
        }

        if (!TryParseTime(request.StartTime, out var start))
        {
            throw new AppValidationException("Bitte eine gültige Startzeit (z. B. 09:00) eintragen.");
        }

        TimeOnly? end = null;
        if (!string.IsNullOrWhiteSpace(request.EndTime))
        {
            if (!TryParseTime(request.EndTime, out var parsedEnd))
            {
                throw new AppValidationException("Bitte eine gültige Endzeit (z. B. 09:45) eintragen.");
            }
            if (parsedEnd <= start)
            {
                throw new AppValidationException("Die Endzeit muss nach der Startzeit liegen.");
            }
            end = parsedEnd;
        }

        var customTitle = NullIfEmpty(request.CustomTitle);
        if (kind == CalendarEventKind.Custom && customTitle is null)
        {
            throw new AppValidationException("Bitte eine eigene Bezeichnung für den Termin eingeben.");
        }

        if (request.StudentId is { } studentId &&
            !await db.Students.AnyAsync(s => s.Id == studentId, ct))
        {
            throw new AppValidationException("Dieser Schüler wurde nicht gefunden. Bitte die Seite neu laden.");
        }

        // Double-booking check (KONZEPT 3.5): no overlapping appointment same day.
        var sameDay = await db.CalendarEvents
            .Where(e => e.DateOn == request.DateOn && e.Id != eventId)
            .ToListAsync(ct);
        var clash = sameDay.FirstOrDefault(e => CalendarRules.Overlaps(start, end, e.StartTime, e.EndTime));
        if (clash is not null)
        {
            throw new AppValidationException(
                $"Zu dieser Zeit gibt es bereits einen Termin ({clash.StartTime:HH\\:mm}" +
                (clash.EndTime is { } ce ? $"–{ce:HH\\:mm}" : "") + "). Bitte eine andere Uhrzeit wählen.");
        }

        return (kind, start, end, customTitle);
    }

    // --- helpers ---

    private async Task<CalendarEventDto> ReloadDtoAsync(Guid id, CancellationToken ct)
    {
        var ev = await db.CalendarEvents.Include(e => e.Student).FirstAsync(e => e.Id == id, ct);
        return ToDto(ev);
    }

    private async Task WriteAuditAsync(Actor actor, string action, CalendarEvent ev, CancellationToken ct)
    {
        var studentName = ev.StudentId is null
            ? null
            : await db.Students.Where(s => s.Id == ev.StudentId)
                .Select(s => s.FirstName + " " + s.LastName).FirstOrDefaultAsync(ct);

        await auditWriter.WriteAsync(actor.UserId, actor.UserName, action,
            "Termin", ev.DateOn.ToString("dd.MM.yyyy"),
            newValuesJson: JsonSerializer.Serialize(new
            {
                Von = ev.StartTime.ToString("HH\\:mm"),
                Bis = ev.EndTime?.ToString("HH\\:mm") ?? "—",
                Art = KindLabel(ev.Kind, ev.CustomTitle),
                Schüler = studentName ?? "—",
            }), cancellationToken: ct);
    }

    private static CalendarEventDto ToDto(CalendarEvent e) => new()
    {
        Id = e.Id,
        DateOn = e.DateOn,
        StartTime = e.StartTime.ToString("HH\\:mm"),
        EndTime = e.EndTime?.ToString("HH\\:mm"),
        Kind = e.Kind.ToString(),
        Title = KindLabel(e.Kind, e.CustomTitle),
        StudentId = e.StudentId,
        StudentName = e.Student is null ? null : $"{e.Student.FirstName} {e.Student.LastName}".Trim(),
        Note = e.Note,
    };

    private static string KindLabel(CalendarEventKind kind, string? customTitle) => kind switch
    {
        CalendarEventKind.Practice => "Praxis",
        CalendarEventKind.Theory => "Theorie",
        CalendarEventKind.Exam => "Prüfung",
        _ => customTitle ?? "Termin",
    };

    private static bool TryParseTime(string value, out TimeOnly time)
        => TimeOnly.TryParse(value, CultureInfo.InvariantCulture, out time);

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
