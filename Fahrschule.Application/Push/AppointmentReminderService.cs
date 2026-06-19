using Fahrschule.Application.Settings;
using Fahrschule.Domain.Entities;
using Fahrschule.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Fahrschule.Application.Push;

public interface IAppointmentReminderService
{
    /// <summary>Sends reminders for appointments that are now due and marks them
    /// as reminded. Returns how many reminders were sent.</summary>
    Task<int> RunAsync(CancellationToken ct = default);
}

/// <summary>
/// Finds appointments that start within the configured lead time (default 30 min)
/// and pushes a sparse reminder to the Fahrlehrer's devices, then marks the
/// appointment as reminded so it is never pushed twice (KONZEPT 3.5). Calendar
/// dates/times are German wall-clock; the server runs UTC, so we convert.
/// </summary>
public class AppointmentReminderService(
    FahrschuleDbContext db,
    ISettingsService settings,
    IPushService push,
    ILogger<AppointmentReminderService> logger) : IAppointmentReminderService
{
    private static readonly TimeZoneInfo BerlinTz = ResolveBerlin();

    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        if (!push.IsConfigured) return 0; // push not set up → nothing to do

        var leadMinutes = (await settings.GetAsync(ct)).AppointmentReminderLeadMinutes;
        var nowUtc = DateTime.UtcNow;
        var windowEndUtc = nowUtc.AddMinutes(leadMinutes);
        var fromDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(nowUtc, BerlinTz).Date);

        // Only still-planned, not-yet-reminded appointments from today on.
        var candidates = await db.CalendarEvents
            .Where(e => !e.Reminded && e.LessonId == null && e.DateOn >= fromDay)
            .ToListAsync(ct);

        var sent = 0;
        foreach (var e in candidates)
        {
            var startUtc = ToUtc(e.DateOn, e.StartTime);
            if (startUtc < nowUtc || startUtc > windowEndUtc) continue; // already past / not due yet

            var minutes = Math.Max(0, (int)Math.Round((startUtc - nowUtc).TotalMinutes));
            var time = e.StartTime.ToString("HH\\:mm");
            // Data minimisation (KONZEPT): time + short kind only, never a name.
            var body = $"Nächster Termin in {minutes} Min: {time} {KindLabel(e)}";

            await push.SendToSubscribersAsync("Termin-Erinnerung", body, "/kalender", ct);
            e.Reminded = true;
            sent++;
        }

        if (sent > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Termin-Erinnerung: {Count} Push gesendet.", sent);
        }
        return sent;
    }

    private static DateTime ToUtc(DateOnly date, TimeOnly time)
    {
        var local = new DateTime(date.Year, date.Month, date.Day, time.Hour, time.Minute, 0, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(local, BerlinTz);
    }

    private static string KindLabel(CalendarEvent e) => e.Kind switch
    {
        CalendarEventKind.Practice => "Praxis",
        CalendarEventKind.Theory => "Theorie",
        CalendarEventKind.Exam => "Prüfung",
        _ => e.CustomTitle ?? "Termin",
    };

    /// <summary>Berlin time works under both Linux ("Europe/Berlin") and Windows
    /// ("W. Europe Standard Time"); falls back to UTC if neither is present.</summary>
    private static TimeZoneInfo ResolveBerlin()
    {
        foreach (var id in new[] { "Europe/Berlin", "W. Europe Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { /* try the next id */ }
        }
        return TimeZoneInfo.Utc;
    }
}
