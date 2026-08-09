using Fahrschule.Application.Audit;
using Fahrschule.Application.LicenseClasses;
using Fahrschule.Application.Settings;
using Fahrschule.Contracts.Settings;
using Fahrschule.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fahrschule.Tests.Settings;

/// <summary>
/// Tests for the settings service. Uses the EF Core in-memory provider so the
/// read/write/validation logic can be tested without a real PostgreSQL.
/// </summary>
public class SettingsServiceTests
{
    private static FahrschuleDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<FahrschuleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new FahrschuleDbContext(options);
    }

    private static SettingsService NewService(FahrschuleDbContext db)
        => new(db, new NullAuditWriter());

    private static readonly Actor TestActor = new(Guid.NewGuid(), "Test");

    [Fact]
    public async Task Get_returns_defaults_when_nothing_is_stored()
    {
        await using var db = NewDb();
        var settings = await NewService(db).GetAsync();

        Assert.Equal(21, settings.DocumentExpiryReminderDays);
        Assert.Equal(30, settings.AppointmentReminderLeadMinutes);
        Assert.Equal(2, settings.ExamLockNormalWeeks);
        Assert.Equal(1, settings.ExamLockShortenedWeeks);
        Assert.Equal(2, settings.ExamLockPracticeLessonsForShortening);
        Assert.Equal(5, settings.RetentionStudentYears);
        Assert.Equal("18:00", settings.LessonTheoryDefaultStartTime);
        Assert.Equal(2, settings.TheoryValidityYears);
        // § 4 Abs. 3 FahrschAusbO: 12 Doppelstunden Grundstoff, 6 bei Vorbesitz.
        Assert.Equal(12, settings.TheoryBasicDoubleLessons);
        Assert.Equal(6, settings.TheoryBasicDoubleLessonsWithPriorLicense);
    }

    [Fact]
    public async Task Update_persists_and_reads_back_the_values()
    {
        await using var db = NewDb();
        var service = NewService(db);

        await service.UpdateAsync(new AppSettingsDto
        {
            DocumentExpiryReminderDays = 14,
            AppointmentReminderLeadMinutes = 60,
            ExamLockNormalWeeks = 3,
            ExamLockShortenedWeeks = 1,
            ExamLockPracticeLessonsForShortening = 4,
            RetentionStudentYears = 6,
            LessonDefaultDurationMinutes = 45,
            LessonDurationPresets = "45, 90",
            LessonTheoryDefaultStartTime = "9:00", // stored tidily as "09:00"
            TheoryValidityYears = 3,
            TheoryBasicDoubleLessons = 12,
            TheoryBasicDoubleLessonsWithPriorLicense = 6,
        }, TestActor);

        var reloaded = await service.GetAsync();
        Assert.Equal(14, reloaded.DocumentExpiryReminderDays);
        Assert.Equal("09:00", reloaded.LessonTheoryDefaultStartTime);
        Assert.Equal(3, reloaded.TheoryValidityYears);
        Assert.Equal(60, reloaded.AppointmentReminderLeadMinutes);
        Assert.Equal(3, reloaded.ExamLockNormalWeeks);
        Assert.Equal(4, reloaded.ExamLockPracticeLessonsForShortening);
        Assert.Equal(6, reloaded.RetentionStudentYears);
        Assert.Equal(45, reloaded.LessonDefaultDurationMinutes);
        Assert.Equal("45, 90", reloaded.LessonDurationPresets);
    }

    [Fact]
    public async Task Duration_presets_are_normalised_sorted_and_deduplicated()
    {
        await using var db = NewDb();
        var service = NewService(db);

        await service.UpdateAsync(ValidDefaults() with { LessonDurationPresets = "90, 45, 45,135" }, TestActor);

        var reloaded = await service.GetAsync();
        Assert.Equal("45, 90, 135", reloaded.LessonDurationPresets);
    }

    [Fact]
    public async Task Update_rejects_an_invalid_duration_preset_list()
    {
        await using var db = NewDb();
        var service = NewService(db);

        await Assert.ThrowsAsync<Fahrschule.Application.Common.AppValidationException>(() =>
            service.UpdateAsync(ValidDefaults() with { LessonDurationPresets = "abc" }, TestActor));
    }

    [Fact]
    public async Task School_master_data_round_trips()
    {
        await using var db = NewDb();
        var service = NewService(db);

        await service.UpdateAsync(new AppSettingsDto
        {
            DocumentExpiryReminderDays = 21,
            AppointmentReminderLeadMinutes = 30,
            ExamLockNormalWeeks = 2,
            ExamLockShortenedWeeks = 1,
            ExamLockPracticeLessonsForShortening = 2,
            RetentionStudentYears = 5,
            LessonDefaultDurationMinutes = 90,
            LessonDurationPresets = "45, 90",
            TheoryBasicDoubleLessons = 12,
            TheoryBasicDoubleLessonsWithPriorLicense = 6,
            SchoolName = "Fahrschule Muster",
            SchoolStreet = "Hauptstr. 1",
            SchoolPostalCode = "04109",
            SchoolCity = "Leipzig",
            SchoolPermitNumber = "AB-123",
            SchoolInstructorNumber = "07",
        }, TestActor);

        var reloaded = await service.GetAsync();
        Assert.Equal("Fahrschule Muster", reloaded.SchoolName);
        Assert.Equal("Hauptstr. 1", reloaded.SchoolStreet);
        Assert.Equal("04109", reloaded.SchoolPostalCode);
        Assert.Equal("Leipzig", reloaded.SchoolCity);
        Assert.Equal("AB-123", reloaded.SchoolPermitNumber);
        Assert.Equal("07", reloaded.SchoolInstructorNumber);
    }

    [Fact]
    public async Task Instructor_number_defaults_to_01_when_blank()
    {
        await using var db = NewDb();
        var settings = await NewService(db).GetAsync();
        Assert.Equal("01", settings.SchoolInstructorNumber);
    }

    [Fact]
    public async Task Update_rejects_an_invalid_theory_start_time()
    {
        await using var db = NewDb();
        var service = NewService(db);

        AppSettingsDto dto = ValidDefaults();
        dto.LessonTheoryDefaultStartTime = "25:99";

        await Assert.ThrowsAsync<Fahrschule.Application.Common.AppValidationException>(() =>
            service.UpdateAsync(dto, TestActor));
    }

    [Fact]
    public async Task Update_rejects_out_of_range_value()
    {
        await using var db = NewDb();
        var service = NewService(db);

        await Assert.ThrowsAsync<Fahrschule.Application.Common.AppValidationException>(() =>
            service.UpdateAsync(ValidDefaults() with { AppointmentReminderLeadMinutes = 1 }, TestActor));
    }

    [Fact]
    public async Task Update_rejects_shortened_lock_longer_than_normal()
    {
        await using var db = NewDb();
        var service = NewService(db);

        await Assert.ThrowsAsync<Fahrschule.Application.Common.AppValidationException>(() =>
            service.UpdateAsync(ValidDefaults() with { ExamLockNormalWeeks = 1, ExamLockShortenedWeeks = 3 }, TestActor));
    }

    /// <summary>A valid settings record we can tweak per test.</summary>
    private static AppSettingsRecord ValidDefaults() => new();

    /// <summary>Small mutable-by-"with" helper mirroring AppSettingsDto for the tests.</summary>
    private record AppSettingsRecord
    {
        public int DocumentExpiryReminderDays { get; init; } = 21;
        public int AppointmentReminderLeadMinutes { get; init; } = 30;
        public int ExamLockNormalWeeks { get; init; } = 2;
        public int ExamLockShortenedWeeks { get; init; } = 1;
        public int ExamLockPracticeLessonsForShortening { get; init; } = 2;
        public int RetentionStudentYears { get; init; } = 5;
        public int LessonDefaultDurationMinutes { get; init; } = 90;
        public string LessonDurationPresets { get; init; } = "45, 90, 135, 180";
        public int TheoryBasicDoubleLessons { get; init; } = 12;
        public int TheoryBasicDoubleLessonsWithPriorLicense { get; init; } = 6;

        public static implicit operator AppSettingsDto(AppSettingsRecord r) => new()
        {
            TheoryBasicDoubleLessons = r.TheoryBasicDoubleLessons,
            TheoryBasicDoubleLessonsWithPriorLicense = r.TheoryBasicDoubleLessonsWithPriorLicense,
            DocumentExpiryReminderDays = r.DocumentExpiryReminderDays,
            AppointmentReminderLeadMinutes = r.AppointmentReminderLeadMinutes,
            ExamLockNormalWeeks = r.ExamLockNormalWeeks,
            ExamLockShortenedWeeks = r.ExamLockShortenedWeeks,
            ExamLockPracticeLessonsForShortening = r.ExamLockPracticeLessonsForShortening,
            RetentionStudentYears = r.RetentionStudentYears,
            LessonDefaultDurationMinutes = r.LessonDefaultDurationMinutes,
            LessonDurationPresets = r.LessonDurationPresets,
        };
    }

    /// <summary>Audit writer that does nothing - keeps these tests focused on settings.</summary>
    private sealed class NullAuditWriter : IAuditWriter
    {
        public Task WriteAsync(Guid? userId, string userName, string action, string entityType,
            string entityId, string? oldValuesJson = null, string? newValuesJson = null,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
