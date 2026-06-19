using Fahrschule.Application.Audit;
using Fahrschule.Application.LicenseClasses;
using Fahrschule.Application.Retention;
using Fahrschule.Application.Settings;
using Fahrschule.Contracts.Settings;
using Fahrschule.Domain.Entities;
using Fahrschule.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fahrschule.Tests.Retention;

/// <summary>
/// Tests for the retention job (KONZEPT rule 7 / § 31 Abs. 3 FahrlG): student
/// records are removed five years after the end of the training year, computed
/// from the last instruction activity - never sooner, and active students are
/// never touched. In-memory provider, fresh context per step (same database).
/// </summary>
public class RetentionServiceTests
{
    private static readonly Actor TestActor = new(Guid.NewGuid(), "Admin Test");
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly DbContextOptions<FahrschuleDbContext> _options =
        new DbContextOptionsBuilder<FahrschuleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    private FahrschuleDbContext NewDb() => new(_options);

    private RetentionService NewService(FahrschuleDbContext db)
    {
        var audit = new AuditWriter(db);
        return new RetentionService(db, new SettingsService(db, audit), audit);
    }

    private async Task SeedAsync(Guid id, int createdYearsAgo, bool deleted = false,
        DateOnly? lesson = null, DateOnly? exam = null)
    {
        await using var db = NewDb();
        db.Students.Add(new Student
        {
            Id = id,
            FirstName = "Max",
            LastName = "Mustermann",
            DateOfBirth = new DateOnly(2000, 1, 1),
            IsDeleted = deleted,
            DeletedAtUtc = deleted ? DateTime.UtcNow : null,
            CreatedAtUtc = DateTime.UtcNow.AddYears(-createdYearsAgo),
            UpdatedAtUtc = DateTime.UtcNow,
        });
        if (lesson is { } lessonDate)
        {
            db.Lessons.Add(new Lesson
            {
                Id = Guid.NewGuid(), StudentId = id, Type = LessonType.Practice,
                DateOn = lessonDate, DurationMinutes = 90, CreatedAtUtc = DateTime.UtcNow,
            });
        }
        if (exam is { } examDate)
        {
            db.Exams.Add(new Exam
            {
                Id = Guid.NewGuid(), StudentId = id, LicenseClassId = Guid.NewGuid(),
                Kind = ExamKind.Practice, DateOn = examDate, Result = ExamResult.Passed,
                CreatedAtUtc = DateTime.UtcNow,
            });
        }
        await db.SaveChangesAsync();
    }

    private async Task<bool> StillExistsAsync(Guid id)
    {
        await using var db = NewDb();
        return await db.Students.IgnoreQueryFilters().AnyAsync(s => s.Id == id);
    }

    private async Task<int> RunAsync(Actor? actor = null)
    {
        await using var db = NewDb();
        return (await NewService(db).RunAsync(actor)).DeletedCount;
    }

    [Fact]
    public async Task Student_whose_last_lesson_is_long_past_is_removed_and_audited()
    {
        var id = Guid.NewGuid();
        await SeedAsync(id, createdYearsAgo: 6, lesson: Today.AddYears(-6));

        Assert.Equal(1, await RunAsync(TestActor));
        Assert.False(await StillExistsAsync(id));

        await using var db = NewDb();
        var entry = await db.AuditLogs.SingleAsync(a => a.Action == "Endgültig gelöscht");
        Assert.Equal("Schüler", entry.EntityType);
        Assert.Equal(id.ToString(), entry.EntityId);
    }

    [Fact]
    public async Task Student_with_recent_activity_is_kept()
    {
        var id = Guid.NewGuid();
        await SeedAsync(id, createdYearsAgo: 6, lesson: Today.AddDays(-1));

        Assert.Equal(0, await RunAsync());
        Assert.True(await StillExistsAsync(id));
    }

    [Fact]
    public async Task Drop_out_with_only_an_old_registration_is_removed()
    {
        // No lesson, no exam → the deadline counts from the registration date.
        var id = Guid.NewGuid();
        await SeedAsync(id, createdYearsAgo: 6);

        Assert.Equal(1, await RunAsync());
        Assert.False(await StillExistsAsync(id));
    }

    [Fact]
    public async Task Recently_registered_student_is_kept()
    {
        var id = Guid.NewGuid();
        await SeedAsync(id, createdYearsAgo: 0);

        Assert.Equal(0, await RunAsync());
        Assert.True(await StillExistsAsync(id));
    }

    [Fact]
    public async Task Soft_deleted_student_with_recent_lesson_is_still_kept()
    {
        // Registered long ago but a lesson happened this year. The lesson is
        // visible only via IgnoreQueryFilters; if it were missed, the deadline
        // would wrongly fall back to the old registration date and delete too early.
        var id = Guid.NewGuid();
        await SeedAsync(id, createdYearsAgo: 6, deleted: true, lesson: Today.AddDays(-1));

        Assert.Equal(0, await RunAsync());
        Assert.True(await StillExistsAsync(id));
    }

    [Fact]
    public async Task Calendar_events_are_removed_with_the_student()
    {
        var id = Guid.NewGuid();
        await SeedAsync(id, createdYearsAgo: 6, lesson: Today.AddYears(-6));

        await using (var db = NewDb())
        {
            db.CalendarEvents.Add(new CalendarEvent
            {
                Id = Guid.NewGuid(), StudentId = id, DateOn = new DateOnly(2026, 6, 1),
                StartTime = new TimeOnly(10, 0), EndTime = new TimeOnly(10, 45),
                Kind = CalendarEventKind.Practice,
            });
            await db.SaveChangesAsync();
        }

        await RunAsync();

        await using var read = NewDb();
        Assert.False(await read.Students.IgnoreQueryFilters().AnyAsync(s => s.Id == id));
        Assert.False(await read.CalendarEvents.AnyAsync(e => e.StudentId == id));
    }

    [Fact]
    public async Task Status_reports_the_period_and_due_students()
    {
        var due = Guid.NewGuid();
        var kept = Guid.NewGuid();
        await SeedAsync(due, createdYearsAgo: 7, exam: Today.AddYears(-6));
        await SeedAsync(kept, createdYearsAgo: 0, lesson: Today.AddDays(-1));

        await using var db = NewDb();
        var status = await NewService(db).GetStatusAsync();

        Assert.Equal(5, status.RetentionYears);
        Assert.Equal(1, status.DueCount);
        var entry = Assert.Single(status.Due);
        Assert.Equal(due, entry.Id);
        Assert.Equal(Today.AddYears(-6).Year, entry.TrainingEndDate.Year);
        Assert.Equal(Today.AddYears(-6).Year + 6, entry.DueDate.Year);
    }

    [Fact]
    public async Task A_shorter_configured_period_makes_more_students_due()
    {
        // Last lesson two years ago: not due under 5 years...
        var id = Guid.NewGuid();
        await SeedAsync(id, createdYearsAgo: 2, lesson: Today.AddYears(-2));
        Assert.Equal(0, await RunAsync());

        // ...but the owner shortens the retention period to 1 year.
        await using (var db = NewDb())
        {
            await new SettingsService(db, new AuditWriter(db)).UpdateAsync(new AppSettingsDto
            {
                DocumentExpiryReminderDays = 21,
                AppointmentReminderLeadMinutes = 30,
                ExamLockNormalWeeks = 2,
                ExamLockShortenedWeeks = 1,
                ExamLockPracticeLessonsForShortening = 2,
                RetentionStudentYears = 1,
                LessonDefaultDurationMinutes = 90,
                LessonDurationPresets = "45, 90, 135, 180",
            }, TestActor);
        }

        Assert.Equal(1, await RunAsync());
        Assert.False(await StillExistsAsync(id));
    }
}
