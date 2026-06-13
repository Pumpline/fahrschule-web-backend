using Fahrschule.Application.Audit;
using Fahrschule.Application.LicenseClasses;
using Fahrschule.Application.Retention;
using Fahrschule.Application.Settings;
using Fahrschule.Application.Students;
using Fahrschule.Contracts.Settings;
using Fahrschule.Domain.Entities;
using Fahrschule.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fahrschule.Tests.Retention;

/// <summary>
/// Tests for the retention job (KONZEPT rule 7): soft-deleted students are
/// removed permanently only after the configured retention period, never before,
/// and active students are never touched. In-memory provider, fresh context per
/// step (same database) - the same pattern as the other service tests.
/// </summary>
public class RetentionServiceTests
{
    private static readonly Actor TestActor = new(Guid.NewGuid(), "Admin Test");

    private readonly DbContextOptions<FahrschuleDbContext> _options =
        new DbContextOptionsBuilder<FahrschuleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    private FahrschuleDbContext NewDb() => new(_options);

    private RetentionService NewService(FahrschuleDbContext db)
    {
        var audit = new AuditWriter(db);
        return new RetentionService(db, new SettingsService(db, audit), new StudentService(db, audit), audit);
    }

    private async Task<Guid> SeedStudentAsync(bool deleted, DateTime? deletedAtUtc = null)
    {
        var id = Guid.NewGuid();
        await using var db = NewDb();
        db.Students.Add(new Student
        {
            Id = id,
            FirstName = "Max",
            LastName = "Mustermann",
            DateOfBirth = new DateOnly(2006, 5, 1),
            IsDeleted = deleted,
            DeletedAtUtc = deletedAtUtc,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<bool> StillExistsAsync(Guid id)
    {
        await using var db = NewDb();
        return await db.Students.IgnoreQueryFilters().AnyAsync(s => s.Id == id);
    }

    [Fact]
    public async Task Student_past_the_deadline_is_removed_permanently_and_audited()
    {
        // Marked for deletion 100 days ago - past the 90-day default.
        var id = await SeedStudentAsync(deleted: true, deletedAtUtc: DateTime.UtcNow.AddDays(-100));

        int deleted;
        await using (var db = NewDb()) deleted = (await NewService(db).RunAsync(TestActor)).DeletedCount;

        Assert.Equal(1, deleted);
        Assert.False(await StillExistsAsync(id));

        // The permanent deletion is recorded in the audit log (rule 1).
        await using (var db = NewDb())
        {
            var entry = await db.AuditLogs.SingleAsync(a => a.Action == "Endgültig gelöscht");
            Assert.Equal("Schüler", entry.EntityType);
            Assert.Equal(id.ToString(), entry.EntityId);
        }
    }

    [Fact]
    public async Task Recently_deleted_student_is_kept()
    {
        // Marked just now - well within the retention window.
        var id = await SeedStudentAsync(deleted: true, deletedAtUtc: DateTime.UtcNow);

        await using (var db = NewDb())
        {
            var result = await NewService(db).RunAsync(TestActor);
            Assert.Equal(0, result.DeletedCount);
        }

        Assert.True(await StillExistsAsync(id));
    }

    [Fact]
    public async Task Active_student_is_never_touched()
    {
        var id = await SeedStudentAsync(deleted: false);

        await using (var db = NewDb())
        {
            Assert.Equal(0, (await NewService(db).RunAsync()).DeletedCount);
        }

        Assert.True(await StillExistsAsync(id));
    }

    [Fact]
    public async Task Calendar_events_are_removed_with_the_student()
    {
        var id = await SeedStudentAsync(deleted: true, deletedAtUtc: DateTime.UtcNow.AddDays(-100));

        await using (var db = NewDb())
        {
            db.CalendarEvents.Add(new CalendarEvent
            {
                Id = Guid.NewGuid(),
                StudentId = id,
                DateOn = new DateOnly(2026, 6, 1),
                StartTime = new TimeOnly(10, 0),
                EndTime = new TimeOnly(10, 45),
                Kind = CalendarEventKind.Practice,
            });
            await db.SaveChangesAsync();
        }

        await using (var db = NewDb()) await NewService(db).RunAsync();

        await using (var read = NewDb())
        {
            Assert.False(await read.Students.IgnoreQueryFilters().AnyAsync(s => s.Id == id));
            Assert.False(await read.CalendarEvents.AnyAsync(e => e.StudentId == id));
        }
    }

    [Fact]
    public async Task Status_reports_the_period_and_each_deadline()
    {
        var recent = await SeedStudentAsync(deleted: true, deletedAtUtc: DateTime.UtcNow);
        var overdue = await SeedStudentAsync(deleted: true, deletedAtUtc: DateTime.UtcNow.AddDays(-100));

        await using var db = NewDb();
        var status = await NewService(db).GetStatusAsync();

        Assert.Equal(90, status.RetentionDays);
        Assert.Equal(2, status.Marked.Count);
        Assert.Equal(1, status.DueCount);

        var overdueEntry = status.Marked.Single(s => s.Id == overdue);
        Assert.True(overdueEntry.IsDue);
        Assert.Equal(overdueEntry.DeletedAtUtc!.Value.AddDays(90), overdueEntry.DueAtUtc);

        Assert.False(status.Marked.Single(s => s.Id == recent).IsDue);
    }

    [Fact]
    public async Task A_shorter_configured_period_makes_more_students_due()
    {
        // Marked 10 days ago - not due under the 90-day default...
        var id = await SeedStudentAsync(deleted: true, deletedAtUtc: DateTime.UtcNow.AddDays(-10));

        // ...but the owner shortens the period to 7 days.
        await using (var db = NewDb())
        {
            await new SettingsService(db, new AuditWriter(db)).UpdateAsync(new AppSettingsDto
            {
                DocumentExpiryReminderDays = 21,
                AppointmentReminderLeadMinutes = 30,
                ExamLockNormalWeeks = 2,
                ExamLockShortenedWeeks = 1,
                ExamLockPracticeLessonsForShortening = 2,
                RetentionStudentDays = 7,
            }, TestActor);
        }

        await using (var db = NewDb())
        {
            Assert.Equal(1, (await NewService(db).RunAsync()).DeletedCount);
        }

        Assert.False(await StillExistsAsync(id));
    }
}
