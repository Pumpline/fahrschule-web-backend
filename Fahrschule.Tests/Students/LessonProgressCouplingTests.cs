using Fahrschule.Application.Audit;
using Fahrschule.Application.Common;
using Fahrschule.Application.LicenseClasses;
using Fahrschule.Application.Students;
using Fahrschule.Contracts.Students;
using Fahrschule.Domain.Entities;
using Fahrschule.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fahrschule.Tests.Students;

/// <summary>
/// Tests for the coupling between lessons and the training progress (the new
/// model): a SIMPLE point is "done" because a lesson covers it (or a manual
/// mark); a COUNTABLE point counts full sessions, while a "practice only"
/// coverage is recorded but does not raise the counter. Editing or deleting a
/// lesson recomputes the affected progress.
/// </summary>
public class LessonProgressCouplingTests
{
    private static readonly Actor Actor = new(Guid.NewGuid(), "Test");

    private readonly DbContextOptions<FahrschuleDbContext> _options =
        new DbContextOptionsBuilder<FahrschuleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    private FahrschuleDbContext NewDb() => new(_options);
    private StudentProgressService Progress(FahrschuleDbContext db) => new(db, new NullAuditWriter());
    private LessonService Lessons(FahrschuleDbContext db) => new(db, new NullAuditWriter());

    private readonly Guid _classB = Guid.NewGuid();
    private readonly Guid _student = Guid.NewGuid();

    private async Task SeedAsync()
    {
        await using var db = NewDb();
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
            Section = "Theorie-Grundstoff", Title = "Thema-1", SortOrder = 1, IsActive = true,
        });
        // A second theory topic so completing ONE does not finish the whole theory
        // section (which would auto-raise the Stand and force-complete it - tested
        // separately). This keeps these tests focused on the lesson↔progress coupling.
        db.CurriculumItems.Add(new CurriculumItem
        {
            Id = Guid.NewGuid(), ItemKey = Guid.NewGuid(), Version = 1, ValidFromUtc = now,
            Section = "Theorie-Grundstoff", Title = "Thema-2", SortOrder = 2, IsActive = true,
        });
        db.CurriculumItems.Add(new CurriculumItem
        {
            Id = Guid.NewGuid(), ItemKey = Guid.NewGuid(), Version = 1, ValidFromUtc = now,
            Section = "Sonderfahrten", Title = "Überlandfahrt", RequiredCount = 3, SortOrder = 3, IsActive = true,
            Classes = [new CurriculumItemClass { LicenseClassId = _classB }],
        });
        await db.SaveChangesAsync();
    }

    private async Task<Guid> ItemIdAsync(string title)
    {
        await using (var db = NewDb()) await Progress(db).GetForStudentAsync(_student); // ensure snapshot
        await using var check = NewDb();
        return await check.StudentProgressItems
            .Where(p => p.StudentId == _student && p.Title == title)
            .Select(p => p.Id).FirstAsync();
    }

    private async Task<ProgressItemDto> ItemAsync(Guid itemId)
    {
        await using var db = NewDb();
        var dto = await Progress(db).GetForStudentAsync(_student);
        return dto.Classes.SelectMany(c => c.Sections).SelectMany(s => s.Items).First(i => i.Id == itemId);
    }

    private CreateLessonRequest Practice(Guid covered, Guid[]? partial = null) => new()
    {
        Type = "Practice", LicenseClassId = _classB, DateOn = new DateOnly(2026, 6, 1),
        StartTime = "09:00", DurationMinutes = 90, CoveredItemIds = [covered],
        PartialPracticeItemIds = partial ?? [],
    };

    [Fact]
    public async Task Lesson_marks_a_simple_point_done_and_deleting_it_reopens()
    {
        await SeedAsync();
        var topic = await ItemIdAsync("Thema-1");

        Guid lessonId;
        await using (var db = NewDb())
        {
            var dto = await Lessons(db).CreateAsync(_student, new CreateLessonRequest
            {
                Type = "Theory", LicenseClassId = _classB, DateOn = new DateOnly(2026, 6, 1),
                StartTime = "18:00", DurationMinutes = 90, CoveredItemIds = [topic],
            }, Actor);
            lessonId = dto.Id;
        }

        var afterCreate = await ItemAsync(topic);
        Assert.True(afterCreate.IsDone);
        Assert.True(afterCreate.CoveredByLesson);
        Assert.False(afterCreate.CompletedManually);

        await using (var db = NewDb()) await Lessons(db).DeleteAsync(_student, lessonId, Actor);

        var afterDelete = await ItemAsync(topic);
        Assert.False(afterDelete.IsDone);
        Assert.False(afterDelete.CoveredByLesson);
    }

    [Fact]
    public async Task A_manually_ticked_point_cannot_be_unticked_once_a_lesson_covers_it()
    {
        await SeedAsync();
        var topic = await ItemIdAsync("Thema-1");

        await using (var db = NewDb())
        {
            await Lessons(db).CreateAsync(_student, new CreateLessonRequest
            {
                Type = "Theory", LicenseClassId = _classB, DateOn = new DateOnly(2026, 6, 1),
                StartTime = "18:00", DurationMinutes = 90, CoveredItemIds = [topic],
            }, Actor);
        }

        await using var db2 = NewDb();
        await Assert.ThrowsAsync<AppValidationException>(() =>
            Progress(db2).SetItemAsync(_student, topic, new SetProgressItemRequest { IsDone = false }, Actor));
    }

    [Fact]
    public async Task Manual_tick_marks_the_point_done_as_a_manual_exception()
    {
        await SeedAsync();
        var topic = await ItemIdAsync("Thema-1");

        await using (var db = NewDb())
        {
            await Progress(db).SetItemAsync(_student, topic,
                new SetProgressItemRequest { IsDone = true }, Actor);
        }

        var item = await ItemAsync(topic);
        Assert.True(item.IsDone);
        Assert.True(item.CompletedManually);
        Assert.False(item.CoveredByLesson);
    }

    [Fact]
    public async Task Full_session_counts_but_practice_only_does_not()
    {
        await SeedAsync();
        var drive = await ItemIdAsync("Überlandfahrt");

        // Full session → counter 1.
        await using (var db = NewDb()) await Lessons(db).CreateAsync(_student, Practice(drive), Actor);
        Assert.Equal(1, (await ItemAsync(drive)).CurrentCount);

        // Practice-only → still recorded (covered) but counter unchanged.
        await using (var db = NewDb())
            await Lessons(db).CreateAsync(_student, Practice(drive, partial: [drive]), Actor);
        Assert.Equal(1, (await ItemAsync(drive)).CurrentCount);
    }

    [Fact]
    public async Task Editing_a_lesson_from_full_to_practice_lowers_the_counter()
    {
        await SeedAsync();
        var drive = await ItemIdAsync("Überlandfahrt");

        Guid lessonId;
        await using (var db = NewDb()) lessonId = (await Lessons(db).CreateAsync(_student, Practice(drive), Actor)).Id;
        Assert.Equal(1, (await ItemAsync(drive)).CurrentCount);

        await using (var db = NewDb())
        {
            await Lessons(db).UpdateAsync(_student, lessonId, new UpdateLessonRequest
            {
                DateOn = new DateOnly(2026, 6, 1), StartTime = "09:00", DurationMinutes = 90,
                CoveredItemIds = [drive], PartialPracticeItemIds = [drive], // now only practice
            }, Actor);
        }

        Assert.Equal(0, (await ItemAsync(drive)).CurrentCount);
    }

    [Fact]
    public async Task Editing_a_lesson_to_add_a_simple_point_marks_it_done()
    {
        await SeedAsync();
        var topic = await ItemIdAsync("Thema-1");

        // A lesson that initially covers nothing.
        Guid lessonId;
        await using (var db = NewDb())
        {
            lessonId = (await Lessons(db).CreateAsync(_student, new CreateLessonRequest
            {
                Type = "Theory", LicenseClassId = _classB, DateOn = new DateOnly(2026, 6, 1),
                StartTime = "18:00", DurationMinutes = 90, CoveredItemIds = [],
            }, Actor)).Id;
        }
        Assert.False((await ItemAsync(topic)).IsDone);

        await using (var db = NewDb())
        {
            await Lessons(db).UpdateAsync(_student, lessonId, new UpdateLessonRequest
            {
                DateOn = new DateOnly(2026, 6, 1), StartTime = "18:00", DurationMinutes = 90,
                CoveredItemIds = [topic],
            }, Actor);
        }

        var item = await ItemAsync(topic);
        Assert.True(item.IsDone);
        Assert.True(item.CoveredByLesson);
    }

    private sealed class NullAuditWriter : IAuditWriter
    {
        public Task WriteAsync(Guid? userId, string userName, string action, string entityType,
            string entityId, string? oldValuesJson = null, string? newValuesJson = null,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
