using Fahrschule.Application.Audit;
using Fahrschule.Application.Common;
using Fahrschule.Application.LicenseClasses;
using Fahrschule.Contracts.Settings;
using Fahrschule.Domain.Entities;
using Fahrschule.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fahrschule.Application.Settings;

/// <summary>
/// One editable setting: its key, default value and an allowed range (used
/// both for validation and for seeding sensible starting values).
/// </summary>
public record SettingDefinition(string Key, int Default, int Min, int Max, string Description);

public interface ISettingsService
{
    Task<AppSettingsDto> GetAsync(CancellationToken ct = default);
    Task<AppSettingsDto> UpdateAsync(AppSettingsDto request, Actor actor, CancellationToken ct = default);
    /// <summary>Seeds any missing setting with its default (called at startup).</summary>
    Task SeedDefaultsAsync(CancellationToken ct = default);
}

/// <summary>
/// Reads and writes the operational settings from the generic <see cref="Setting"/>
/// table. Each value has a defined default and an allowed range; out-of-range
/// input is rejected with an understandable German message. Changes are audited.
/// </summary>
public class SettingsService(FahrschuleDbContext db, IAuditWriter auditWriter) : ISettingsService
{
    // The catalogue of known settings. Adding a new operational value later
    // means adding one definition here (plus a field in AppSettingsDto).
    public const string DocumentExpiryReminderDays = "Reminder.DocumentExpiryDays";
    public const string AppointmentReminderLeadMinutes = "Reminder.AppointmentLeadMinutes";
    public const string ExamLockNormalWeeks = "ExamLock.NormalWeeks";
    public const string ExamLockShortenedWeeks = "ExamLock.ShortenedWeeks";
    public const string ExamLockPracticeLessonsForShortening = "ExamLock.PracticeLessonsForShortening";

    private static readonly SettingDefinition[] Definitions =
    [
        new(DocumentExpiryReminderDays, 21, 1, 365, "Tage vor Ablauf einer Unterlage, ab wann erinnert wird"),
        new(AppointmentReminderLeadMinutes, 30, 5, 240, "Minuten vor einem Termin für die Push-Erinnerung"),
        new(ExamLockNormalWeeks, 2, 1, 12, "Normale Wiederholungssperre nach Fehlversuch (Wochen)"),
        new(ExamLockShortenedWeeks, 1, 0, 12, "Verkürzte Sperre mit Zusatzstunden (Wochen)"),
        new(ExamLockPracticeLessonsForShortening, 2, 0, 20, "Zusatzstunden für die verkürzte Sperre"),
    ];

    public async Task<AppSettingsDto> GetAsync(CancellationToken ct = default)
    {
        var values = await db.Settings.ToDictionaryAsync(s => s.Key, s => s.Value, ct);

        int Read(string key) => values.TryGetValue(key, out var raw) && int.TryParse(raw, out var v)
            ? v
            : Definitions.First(d => d.Key == key).Default;

        return new AppSettingsDto
        {
            DocumentExpiryReminderDays = Read(DocumentExpiryReminderDays),
            AppointmentReminderLeadMinutes = Read(AppointmentReminderLeadMinutes),
            ExamLockNormalWeeks = Read(ExamLockNormalWeeks),
            ExamLockShortenedWeeks = Read(ExamLockShortenedWeeks),
            ExamLockPracticeLessonsForShortening = Read(ExamLockPracticeLessonsForShortening),
        };
    }

    public async Task<AppSettingsDto> UpdateAsync(AppSettingsDto request, Actor actor, CancellationToken ct = default)
    {
        var incoming = new Dictionary<string, int>
        {
            [DocumentExpiryReminderDays] = request.DocumentExpiryReminderDays,
            [AppointmentReminderLeadMinutes] = request.AppointmentReminderLeadMinutes,
            [ExamLockNormalWeeks] = request.ExamLockNormalWeeks,
            [ExamLockShortenedWeeks] = request.ExamLockShortenedWeeks,
            [ExamLockPracticeLessonsForShortening] = request.ExamLockPracticeLessonsForShortening,
        };

        // Validate every value against its allowed range first (all-or-nothing).
        var errors = new List<string>();
        foreach (var def in Definitions)
        {
            var value = incoming[def.Key];
            if (value < def.Min || value > def.Max)
            {
                errors.Add($"„{def.Description}“ muss zwischen {def.Min} und {def.Max} liegen.");
            }
        }
        // Business-sensible relation: the shortened lock must not exceed the normal one.
        if (request.ExamLockShortenedWeeks > request.ExamLockNormalWeeks)
        {
            errors.Add("Die verkürzte Sperre darf nicht länger als die normale Sperre sein.");
        }
        if (errors.Count > 0)
        {
            throw new AppValidationException(string.Join(" ", errors));
        }

        var now = DateTime.UtcNow;
        foreach (var def in Definitions)
        {
            var newValue = incoming[def.Key].ToString();
            var existing = await db.Settings.FirstOrDefaultAsync(s => s.Key == def.Key, ct);
            if (existing is null)
            {
                db.Settings.Add(new Setting { Key = def.Key, Value = newValue, Description = def.Description, UpdatedAtUtc = now });
            }
            else
            {
                existing.Value = newValue;
                existing.UpdatedAtUtc = now;
            }
        }
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(actor.UserId, actor.UserName, "Geändert",
            "Einstellungen", "Betrieb", cancellationToken: ct);

        return await GetAsync(ct);
    }

    public async Task SeedDefaultsAsync(CancellationToken ct = default)
    {
        var existingKeys = await db.Settings.Select(s => s.Key).ToListAsync(ct);
        var now = DateTime.UtcNow;
        var added = false;

        foreach (var def in Definitions.Where(d => !existingKeys.Contains(d.Key)))
        {
            db.Settings.Add(new Setting
            {
                Key = def.Key,
                Value = def.Default.ToString(),
                Description = def.Description,
                UpdatedAtUtc = now,
            });
            added = true;
        }

        if (added)
        {
            await db.SaveChangesAsync(ct);
        }
    }
}
