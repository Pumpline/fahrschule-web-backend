using Fahrschule.Application.Audit;
using Fahrschule.Application.Common;
using Fahrschule.Application.LicenseClasses;
using Fahrschule.Application.Payments;
using Fahrschule.Application.Settings;
using Fahrschule.Application.Students;
using Fahrschule.Contracts.Students;
using Fahrschule.Domain.Entities;
using Fahrschule.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fahrschule.Tests.Students;

/// <summary>
/// Tests for entering lessons. Uses the EF Core in-memory provider (a fresh
/// context per call, same database) so creating a lesson and its effect on the
/// progress is exercised end to end without a real PostgreSQL.
/// </summary>
public class LessonServiceTests
{
    private static readonly Actor TestActor = new(Guid.NewGuid(), "Test");

    private readonly DbContextOptions<FahrschuleDbContext> _options =
        new DbContextOptionsBuilder<FahrschuleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    private FahrschuleDbContext NewDb() => new(_options);
    private LessonService NewService(FahrschuleDbContext db) => new(db, new NullAuditWriter(), NewPaymentService(db));

    private readonly Guid _classB = Guid.NewGuid();
    private readonly Guid _student = Guid.NewGuid();

    /// <summary>Seeds a class B, a student in B, and the curriculum (a shared
    /// theory topic + a countable special drive for B), then returns the
    /// created snapshot via the progress service.</summary>
    private async Task<StudentProgressDto> SeedAndSnapshotAsync()
    {
        await using (var db = NewDb())
        {
            db.LicenseClasses.Add(new LicenseClass { Id = _classB, Code = "B", SortOrder = 1, IsActive = true });
            db.Students.Add(new Student
            {
                Id = _student, FirstName = "Max", LastName = "Muster",
                LicenseClasses = { new StudentLicenseClass { LicenseClassId = _classB, Phase = StudentPhase.Theory } },
            });
            var now = DateTime.UtcNow;
            db.CurriculumItems.Add(new CurriculumItem
            {
                Id = Guid.NewGuid(), ItemKey = Guid.NewGuid(), Version = 1, ValidFromUtc = now,
                Section = "Theorie-Grundstoff", Title = "Grundstoff-Thema", SortOrder = 1, IsActive = true,
            });
            db.CurriculumItems.Add(new CurriculumItem
            {
                Id = Guid.NewGuid(), ItemKey = Guid.NewGuid(), Version = 1, ValidFromUtc = now,
                Section = "Sonderfahrten", Title = "Ueberlandfahrt", RequiredCount = 5, SortOrder = 2, IsActive = true,
                Classes = [new CurriculumItemClass { LicenseClassId = _classB }],
            });
            await db.SaveChangesAsync();
        }

        await using var snap = NewDb();
        return await new StudentProgressService(snap, new NullAuditWriter()).GetForStudentAsync(_student);
    }

    private static ProgressItemDto Item(StudentProgressDto dto, string title)
        => dto.Classes.SelectMany(c => c.Sections).SelectMany(s => s.Items).First(i => i.Title == title);

    [Fact]
    public async Task Create_ticks_simple_point_and_counts_countable_point()
    {
        var progress = await SeedAndSnapshotAsync();
        var topicId = Item(progress, "Grundstoff-Thema").Id;
        var driveId = Item(progress, "Ueberlandfahrt").Id;
        var date = new DateOnly(2026, 6, 13);

        await using (var db = NewDb())
        {
            await NewService(db).CreateAsync(_student, new CreateLessonRequest
            {
                Type = "Practice", LicenseClassId = _classB, DateOn = date, StartTime = "14:30", DurationMinutes = 90,
                CoveredItemIds = [topicId, driveId],
            }, TestActor);
        }

        // The lesson was stored with both covered points linked.
        await using (var check = NewDb())
        {
            var lesson = await check.Lessons.Include(l => l.Items).FirstAsync();
            Assert.Equal(LessonType.Practice, lesson.Type);
            Assert.Equal(new TimeOnly(14, 30), lesson.StartTime);
            Assert.Equal(90, lesson.DurationMinutes);
            Assert.Equal(2, lesson.Items.Count);
        }

        // The effect reached the progress: simple ticked, countable at 1/5.
        await using var after = NewDb();
        var refreshed = await new StudentProgressService(after, new NullAuditWriter()).GetForStudentAsync(_student);
        var topic = Item(refreshed, "Grundstoff-Thema");
        var drive = Item(refreshed, "Ueberlandfahrt");
        Assert.True(topic.IsDone);
        Assert.Equal(date, topic.CompletedOn);
        Assert.Equal(1, drive.CurrentCount);
    }

    [Fact]
    public async Task Create_rejects_an_unknown_type()
    {
        var progress = await SeedAndSnapshotAsync();
        var topicId = Item(progress, "Grundstoff-Thema").Id;

        await using var db = NewDb();
        await Assert.ThrowsAsync<AppValidationException>(() => NewService(db).CreateAsync(_student,
            new CreateLessonRequest { Type = "Quatsch", DateOn = new DateOnly(2026, 6, 13), DurationMinutes = 90, CoveredItemIds = [topicId] },
            TestActor));
    }

    [Fact]
    public async Task Create_rejects_zero_duration()
    {
        await SeedAndSnapshotAsync();
        await using var db = NewDb();
        await Assert.ThrowsAsync<AppValidationException>(() => NewService(db).CreateAsync(_student,
            new CreateLessonRequest { Type = "Theory", DateOn = new DateOnly(2026, 6, 13), DurationMinutes = 0, CoveredItemIds = [] },
            TestActor));
    }

    [Fact]
    public async Task Create_rejects_a_class_the_student_does_not_have()
    {
        await SeedAndSnapshotAsync();
        await using var db = NewDb();
        await Assert.ThrowsAsync<AppValidationException>(() => NewService(db).CreateAsync(_student,
            new CreateLessonRequest { Type = "Theory", LicenseClassId = Guid.NewGuid(), DateOn = new DateOnly(2026, 6, 13), StartTime = "18:00", DurationMinutes = 90, CoveredItemIds = [] },
            TestActor));
    }

    [Fact]
    public async Task GetForStudent_returns_entered_lessons()
    {
        var progress = await SeedAndSnapshotAsync();
        var topicId = Item(progress, "Grundstoff-Thema").Id;

        await using (var db = NewDb())
        {
            await NewService(db).CreateAsync(_student, new CreateLessonRequest
            {
                Type = "Theory", LicenseClassId = null, DateOn = new DateOnly(2026, 6, 10), StartTime = "18:00", DurationMinutes = 90,
                CoveredItemIds = [topicId],
            }, TestActor);
        }

        await using var read = NewDb();
        var lessons = await NewService(read).GetForStudentAsync(_student);
        Assert.Single(lessons);
        Assert.Equal("Grundstoff", lessons[0].ClassLabel);
        Assert.Equal("18:00", lessons[0].StartTime);
        Assert.Contains("Grundstoff-Thema", lessons[0].CoveredTitles);
    }

    [Fact]
    public async Task Create_rejects_a_missing_start_time()
    {
        await SeedAndSnapshotAsync();
        await using var db = NewDb();
        await Assert.ThrowsAsync<AppValidationException>(() => NewService(db).CreateAsync(_student,
            new CreateLessonRequest { Type = "Theory", DateOn = new DateOnly(2026, 6, 13), StartTime = "", DurationMinutes = 90, CoveredItemIds = [] },
            TestActor));
    }

    [Fact]
    public async Task Update_changes_the_lessons_own_fields()
    {
        var progress = await SeedAndSnapshotAsync();
        var topicId = Item(progress, "Grundstoff-Thema").Id;

        Guid lessonId;
        await using (var db = NewDb())
        {
            lessonId = (await NewService(db).CreateAsync(_student, new CreateLessonRequest
            {
                Type = "Theory", LicenseClassId = null, DateOn = new DateOnly(2026, 6, 10), StartTime = "18:00", DurationMinutes = 90,
                CoveredItemIds = [topicId],
            }, TestActor)).Id;
        }

        await using (var db = NewDb())
        {
            await NewService(db).UpdateAsync(_student, lessonId, new UpdateLessonRequest
            {
                DateOn = new DateOnly(2026, 6, 11), StartTime = "19:15", DurationMinutes = 45, Note = "verschoben",
            }, TestActor);
        }

        await using var read = NewDb();
        var lesson = await read.Lessons.FirstAsync(l => l.Id == lessonId);
        Assert.Equal(new DateOnly(2026, 6, 11), lesson.DateOn);
        Assert.Equal(new TimeOnly(19, 15), lesson.StartTime);
        Assert.Equal(45, lesson.DurationMinutes);
        Assert.Equal("verschoben", lesson.Note);
    }

    [Fact]
    public async Task Delete_hides_the_lesson_from_the_list()
    {
        var progress = await SeedAndSnapshotAsync();
        var topicId = Item(progress, "Grundstoff-Thema").Id;

        Guid lessonId;
        await using (var db = NewDb())
        {
            lessonId = (await NewService(db).CreateAsync(_student, new CreateLessonRequest
            {
                Type = "Theory", LicenseClassId = null, DateOn = new DateOnly(2026, 6, 10), StartTime = "18:00", DurationMinutes = 90,
                CoveredItemIds = [topicId],
            }, TestActor)).Id;
        }

        await using (var db = NewDb())
        {
            await NewService(db).DeleteAsync(_student, lessonId, TestActor);
        }

        await using var read = NewDb();
        // Gone from the hours list, but still present (soft-deleted) in the table.
        Assert.Empty(await NewService(read).GetForStudentAsync(_student));
        Assert.True((await read.Lessons.IgnoreQueryFilters().FirstAsync(l => l.Id == lessonId)).IsDeleted);
    }

    [Fact]
    public async Task Create_with_a_calendar_event_marks_it_durchgefuehrt()
    {
        var progress = await SeedAndSnapshotAsync();
        var topicId = Item(progress, "Grundstoff-Thema").Id;
        var eventId = Guid.NewGuid();

        await using (var db = NewDb())
        {
            db.CalendarEvents.Add(new CalendarEvent
            {
                Id = eventId, StudentId = _student, DateOn = new DateOnly(2026, 6, 13),
                StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(10, 30), Kind = CalendarEventKind.Practice,
            });
            await db.SaveChangesAsync();
        }

        Guid lessonId;
        await using (var db = NewDb())
        {
            lessonId = (await NewService(db).CreateAsync(_student, new CreateLessonRequest
            {
                Type = "Practice", LicenseClassId = _classB, DateOn = new DateOnly(2026, 6, 13), StartTime = "09:00", DurationMinutes = 90,
                CoveredItemIds = [topicId], CalendarEventId = eventId,
            }, TestActor)).Id;
        }

        await using var check = NewDb();
        var ev = await check.CalendarEvents.FirstAsync(e => e.Id == eventId);
        Assert.Equal(lessonId, ev.LessonId);
    }

    [Fact]
    public async Task Create_does_not_link_a_calendar_event_of_another_student()
    {
        var progress = await SeedAndSnapshotAsync();
        var topicId = Item(progress, "Grundstoff-Thema").Id;
        var eventId = Guid.NewGuid();
        var otherStudent = Guid.NewGuid();

        await using (var db = NewDb())
        {
            db.Students.Add(new Student { Id = otherStudent, FirstName = "Andere", LastName = "Person" });
            db.CalendarEvents.Add(new CalendarEvent
            {
                Id = eventId, StudentId = otherStudent, DateOn = new DateOnly(2026, 6, 13),
                StartTime = new TimeOnly(9, 0), Kind = CalendarEventKind.Practice,
            });
            await db.SaveChangesAsync();
        }

        await using (var db = NewDb())
        {
            await NewService(db).CreateAsync(_student, new CreateLessonRequest
            {
                Type = "Practice", LicenseClassId = _classB, DateOn = new DateOnly(2026, 6, 13), StartTime = "09:00", DurationMinutes = 90,
                CoveredItemIds = [topicId], CalendarEventId = eventId,
            }, TestActor);
        }

        await using var check = NewDb();
        var ev = await check.CalendarEvents.FirstAsync(e => e.Id == eventId);
        Assert.Null(ev.LessonId);
    }

    /// <summary>Audit writer that does nothing - keeps these tests focused.</summary>
    private sealed class NullAuditWriter : IAuditWriter
    {
        public Task WriteAsync(Guid? userId, string userName, string action, string entityType,
            string entityId, string? oldValuesJson = null, string? newValuesJson = null,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    /// <summary>The lesson service also needs the payment service (money paid for
    /// a lesson, KONZEPT 3.6). These tests do not check money, so a plain
    /// instance on the same database is enough.</summary>
    private static PaymentService NewPaymentService(FahrschuleDbContext db)
        => new(db, new SettingsService(db, new NullAuditWriter()), new NullAuditWriter(), NullLogger<PaymentService>.Instance);

}
