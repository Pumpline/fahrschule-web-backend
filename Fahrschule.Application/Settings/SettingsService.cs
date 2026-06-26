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

/// <summary>A free-text setting (e.g. the driving-school master data).</summary>
public record StringSettingDefinition(string Key, int MaxLength, string Description);

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
    public const string RetentionStudentYears = "Retention.StudentYears";
    public const string LessonDefaultDurationMinutes = "Lesson.DefaultDurationMinutes";

    private static readonly SettingDefinition[] Definitions =
    [
        new(DocumentExpiryReminderDays, 21, 1, 365, "Tage vor Ablauf einer Unterlage, ab wann erinnert wird"),
        new(AppointmentReminderLeadMinutes, 30, 5, 240, "Minuten vor einem Termin für die Push-Erinnerung"),
        new(ExamLockNormalWeeks, 2, 1, 12, "Normale Wiederholungssperre nach Fehlversuch (Wochen)"),
        new(ExamLockShortenedWeeks, 1, 0, 12, "Verkürzte Sperre mit Zusatzstunden (Wochen)"),
        new(ExamLockPracticeLessonsForShortening, 2, 0, 20, "Zusatzstunden für die verkürzte Sperre"),
        new(LessonDefaultDurationMinutes, 90, 5, 600, "Vorausgewählte Dauer beim Stunden-Eintragen (Minuten)"),
        // § 31 Abs. 3 FahrlG: 5 years after the end of the training year. Range
        // 1-30 leaves room should the law change; default 5 is the current value.
        new(RetentionStudentYears, 5, 1, 30, "Aufbewahrungsfrist für Schüler-Daten nach Ausbildungsende (Jahre, § 31 FahrlG)"),
    ];

    // Driving-school master data (free text, KONZEPT 1b) - shown on the
    // Ausbildungsnachweis. Empty by default (the owner fills these in).
    public const string SchoolName = "School.Name";
    public const string SchoolStreet = "School.Street";
    public const string SchoolPostalCode = "School.PostalCode";
    public const string SchoolCity = "School.City";
    public const string SchoolPermitNumber = "School.PermitNumber";

    // Fahrlehrer number for the training record's "FL" column. One instructor
    // (the owner), so it pre-fills every row; editable (rule 3), default "01".
    public const string SchoolInstructorNumber = "School.InstructorNumber";
    public const string DefaultInstructorNumber = "01";

    // Quick-pick lesson durations (minutes), comma-separated and editable
    // (project rule 3: data, not code). The "Stunde eintragen" dialog shows
    // these as buttons; any other duration can still be typed freely.
    public const string LessonDurationPresets = "Lesson.DurationPresets";
    public const string DefaultLessonDurationPresets = "45, 90, 135, 180";

    private static readonly StringSettingDefinition[] StringDefinitions =
    [
        new(SchoolName, 200, "Name der Fahrschule"),
        new(SchoolStreet, 200, "Straße und Hausnummer"),
        new(SchoolPostalCode, 20, "Postleitzahl"),
        new(SchoolCity, 100, "Ort"),
        new(SchoolPermitNumber, 100, "Erlaubnisnummer"),
        new(SchoolInstructorNumber, 10, "Fahrlehrer-Nummer (FL) für den Ausbildungsnachweis"),
        new(LessonDurationPresets, 200, "Schnell-Auswahl der Stundendauern (Minuten, mit Komma getrennt)"),
    ];

    public async Task<AppSettingsDto> GetAsync(CancellationToken ct = default)
    {
        var values = await db.Settings.ToDictionaryAsync(s => s.Key, s => s.Value, ct);

        int Read(string key) => values.TryGetValue(key, out var raw) && int.TryParse(raw, out var v)
            ? v
            : Definitions.First(d => d.Key == key).Default;
        string ReadText(string key) => values.GetValueOrDefault(key) ?? string.Empty;

        return new AppSettingsDto
        {
            DocumentExpiryReminderDays = Read(DocumentExpiryReminderDays),
            AppointmentReminderLeadMinutes = Read(AppointmentReminderLeadMinutes),
            ExamLockNormalWeeks = Read(ExamLockNormalWeeks),
            ExamLockShortenedWeeks = Read(ExamLockShortenedWeeks),
            ExamLockPracticeLessonsForShortening = Read(ExamLockPracticeLessonsForShortening),
            RetentionStudentYears = Read(RetentionStudentYears),
            LessonDefaultDurationMinutes = Read(LessonDefaultDurationMinutes),
            // Fall back to the sensible default list when nothing was saved yet.
            LessonDurationPresets = NullIfEmpty(ReadText(LessonDurationPresets)) ?? DefaultLessonDurationPresets,
            SchoolName = ReadText(SchoolName),
            SchoolStreet = ReadText(SchoolStreet),
            SchoolPostalCode = ReadText(SchoolPostalCode),
            SchoolCity = ReadText(SchoolCity),
            SchoolPermitNumber = ReadText(SchoolPermitNumber),
            // Falls back to "01" so the FL column is never empty (single instructor).
            SchoolInstructorNumber = NullIfEmpty(ReadText(SchoolInstructorNumber)) ?? DefaultInstructorNumber,
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
            [RetentionStudentYears] = request.RetentionStudentYears,
            [LessonDefaultDurationMinutes] = request.LessonDefaultDurationMinutes,
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

        // Lesson duration presets: parse + normalise the comma-separated list.
        // On success we write the cleaned-up form back so it is stored tidily.
        if (NormalizeDurationPresets(request.LessonDurationPresets, out var normalizedPresets, out var presetError))
        {
            request.LessonDurationPresets = normalizedPresets;
        }
        else
        {
            errors.Add(presetError!);
        }

        // Free-text school data: only a length check (empty is allowed).
        var incomingText = StringValues(request);
        foreach (var def in StringDefinitions)
        {
            if ((incomingText[def.Key]?.Length ?? 0) > def.MaxLength)
            {
                errors.Add($"„{def.Description}“ darf höchstens {def.MaxLength} Zeichen lang sein.");
            }
        }

        if (errors.Count > 0)
        {
            throw new AppValidationException(string.Join(" ", errors));
        }

        var now = DateTime.UtcNow;
        foreach (var def in Definitions)
        {
            await UpsertAsync(def.Key, incoming[def.Key].ToString(), def.Description, now, ct);
        }
        foreach (var def in StringDefinitions)
        {
            await UpsertAsync(def.Key, incomingText[def.Key]?.Trim() ?? string.Empty, def.Description, now, ct);
        }
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(actor.UserId, actor.UserName, "Geändert",
            "Einstellungen", "Betrieb", cancellationToken: ct);

        return await GetAsync(ct);
    }

    private static Dictionary<string, string?> StringValues(AppSettingsDto r) => new()
    {
        [SchoolName] = r.SchoolName,
        [SchoolStreet] = r.SchoolStreet,
        [SchoolPostalCode] = r.SchoolPostalCode,
        [SchoolCity] = r.SchoolCity,
        [SchoolPermitNumber] = r.SchoolPermitNumber,
        [SchoolInstructorNumber] = r.SchoolInstructorNumber,
        [LessonDurationPresets] = r.LessonDurationPresets,
    };

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// Parses the comma-separated duration list, validates each value and returns
    /// a tidy, de-duplicated, sorted form (e.g. "45, 90, 135"). Returns false with
    /// a German error message when something is off.
    /// </summary>
    private static bool NormalizeDurationPresets(string? raw, out string normalized, out string? error)
    {
        normalized = string.Empty;
        error = null;

        var parts = (raw ?? string.Empty)
            .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length == 0)
        {
            error = "Bitte mindestens eine Stundendauer eintragen (z. B. 45, 90).";
            return false;
        }
        if (parts.Length > 12)
        {
            error = "Bitte höchstens 12 Stundendauern eintragen.";
            return false;
        }

        var values = new SortedSet<int>();
        foreach (var part in parts)
        {
            if (!int.TryParse(part, out var minutes))
            {
                error = "Bitte die Stundendauern als ganze Zahlen eintragen (z. B. 45, 90, 135).";
                return false;
            }
            if (minutes < 5 || minutes > 600)
            {
                error = "Jede Stundendauer muss zwischen 5 und 600 Minuten liegen.";
                return false;
            }
            values.Add(minutes);
        }

        normalized = string.Join(", ", values);
        return true;
    }

    private async Task UpsertAsync(string key, string value, string description, DateTime now, CancellationToken ct)
    {
        var existing = await db.Settings.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (existing is null)
        {
            db.Settings.Add(new Setting { Key = key, Value = value, Description = description, UpdatedAtUtc = now });
        }
        else
        {
            existing.Value = value;
            existing.UpdatedAtUtc = now;
        }
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
