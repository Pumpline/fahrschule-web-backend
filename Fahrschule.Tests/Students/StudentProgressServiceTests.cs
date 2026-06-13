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
/// Tests for the training-progress service. Uses the EF Core in-memory provider
/// (a fresh context per call, same database) so the snapshot + counting logic is
/// exercised end to end without a real PostgreSQL.
/// </summary>
public class StudentProgressServiceTests
{
    private static readonly Actor TestActor = new(Guid.NewGuid(), "Test");

    private readonly DbContextOptions<FahrschuleDbContext> _options =
        new DbContextOptionsBuilder<FahrschuleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    private FahrschuleDbContext NewDb() => new(_options);
    private StudentProgressService NewService(FahrschuleDbContext db) => new(db, new NullAuditWriter());

    // --- ids shared across the seed ---
    private readonly Guid _classB = Guid.NewGuid();
    private readonly Guid _classA1 = Guid.NewGuid();
    private readonly Guid _student = Guid.NewGuid();

    /// <summary>Seeds two classes (B, A1), a student in both, and three plan
    /// points: a shared theory topic (all classes), a B-only topic, and a
    /// countable special drive (B-only, target 3).</summary>
    private async Task SeedAsync()
    {
        await using var db = NewDb();

        db.LicenseClasses.Add(new LicenseClass { Id = _classB, Code = "B", SortOrder = 1, IsActive = true });
        db.LicenseClasses.Add(new LicenseClass { Id = _classA1, Code = "A1", SortOrder = 2, IsActive = true });

        db.Students.Add(new Student
        {
            Id = _student,
            FirstName = "Max",
            LastName = "Muster",
            LicenseClasses =
            {
                new StudentLicenseClass { LicenseClassId = _classB, Phase = StudentPhase.Theory },
                new StudentLicenseClass { LicenseClassId = _classA1, Phase = StudentPhase.Theory },
            },
        });

        var now = DateTime.UtcNow;
        db.CurriculumItems.Add(new CurriculumItem
        {
            Id = Guid.NewGuid(), ItemKey = Guid.NewGuid(), Version = 1, ValidFromUtc = now,
            Section = "Theorie-Grundstoff", Title = "Grundstoff-Thema", SortOrder = 1, IsActive = true,
            // no classes → applies to all of the student's classes (shared)
        });
        db.CurriculumItems.Add(new CurriculumItem
        {
            Id = Guid.NewGuid(), ItemKey = Guid.NewGuid(), Version = 1, ValidFromUtc = now,
            Section = "Theorie-Zusatzstoff", Title = "B-Zusatz", SortOrder = 2, IsActive = true,
            Classes = [new CurriculumItemClass { LicenseClassId = _classB }],
        });
        db.CurriculumItems.Add(new CurriculumItem
        {
            Id = Guid.NewGuid(), ItemKey = Guid.NewGuid(), Version = 1, ValidFromUtc = now,
            Section = "Sonderfahrten", Title = "Überlandfahrt", RequiredCount = 3, SortOrder = 3, IsActive = true,
            Classes = [new CurriculumItemClass { LicenseClassId = _classB }],
        });

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Get_creates_the_snapshot_and_groups_per_class()
    {
        await SeedAsync();
        await using var db = NewDb();

        var result = await NewService(db).GetForStudentAsync(_student);

        Assert.Equal(2, result.Classes.Count);

        var b = result.Classes.Single(c => c.Code == "B");
        var a1 = result.Classes.Single(c => c.Code == "A1");

        // B gets all three points; A1 only the shared theory topic.
        Assert.Equal(3, b.TotalCount);
        Assert.Equal(1, a1.TotalCount);

        // The shared topic is flagged as shared (student has two classes).
        var sharedInA1 = a1.Sections.SelectMany(s => s.Items).Single();
        Assert.Equal("Grundstoff-Thema", sharedInA1.Title);
        Assert.True(sharedInA1.IsShared);

        // The snapshot rows were persisted.
        await using var check = NewDb();
        Assert.Equal(3, await check.StudentProgressItems.CountAsync(p => p.StudentId == _student));
    }

    [Fact]
    public async Task Get_is_idempotent_and_does_not_duplicate_the_snapshot()
    {
        await SeedAsync();

        await using (var db1 = NewDb()) await NewService(db1).GetForStudentAsync(_student);
        await using (var db2 = NewDb()) await NewService(db2).GetForStudentAsync(_student);

        await using var check = NewDb();
        Assert.Equal(3, await check.StudentProgressItems.CountAsync(p => p.StudentId == _student));
    }

    [Fact]
    public async Task SetItem_ticks_a_simple_point_and_updates_the_percentage()
    {
        await SeedAsync();

        Guid topicId;
        await using (var db = NewDb())
        {
            var progress = await NewService(db).GetForStudentAsync(_student);
            topicId = ItemByTitle(progress, "Grundstoff-Thema").Id;
        }

        await using var db2 = NewDb();
        var updated = await NewService(db2).SetItemAsync(_student, topicId,
            new SetProgressItemRequest { IsDone = true }, TestActor);

        var item = ItemByTitle(updated, "Grundstoff-Thema");
        Assert.True(item.IsDone);
        Assert.NotNull(item.CompletedOn); // defaults to today

        // 1 of 1 done for A1 → 100%; 1 of 3 done for B → 33%.
        Assert.Equal(100, updated.Classes.Single(c => c.Code == "A1").DonePercent);
        Assert.Equal(33, updated.Classes.Single(c => c.Code == "B").DonePercent);
    }

    [Fact]
    public async Task SetItem_rejects_a_countable_point()
    {
        await SeedAsync();

        Guid driveId;
        await using (var db = NewDb())
            driveId = ItemByTitle(await NewService(db).GetForStudentAsync(_student), "Überlandfahrt").Id;

        await using var db2 = NewDb();
        await Assert.ThrowsAsync<AppValidationException>(() =>
            NewService(db2).SetItemAsync(_student, driveId, new SetProgressItemRequest { IsDone = true }, TestActor));
    }

    [Fact]
    public async Task Counter_adds_entries_until_done_then_removing_one_reopens_it()
    {
        await SeedAsync();

        Guid driveId;
        await using (var db = NewDb())
            driveId = ItemByTitle(await NewService(db).GetForStudentAsync(_student), "Überlandfahrt").Id;

        // Add three sessions → reaches the target of 3 → done.
        var date = new DateOnly(2026, 6, 13);
        StudentProgressDto afterThree = null!;
        for (var i = 0; i < 3; i++)
        {
            await using var db = NewDb();
            afterThree = await NewService(db).AddEntryAsync(_student, driveId,
                new AddProgressEntryRequest { PerformedOn = date }, TestActor);
        }

        var drive = ItemByTitle(afterThree, "Überlandfahrt");
        Assert.Equal(3, drive.CurrentCount);
        Assert.True(drive.IsDone);
        Assert.Equal(3, drive.Entries.Count);

        // Remove one → back to 2 of 3 → no longer done.
        await using var db3 = NewDb();
        var afterRemove = await NewService(db3).RemoveEntryAsync(_student, driveId, drive.Entries[0].Id, TestActor);

        var driveAgain = ItemByTitle(afterRemove, "Überlandfahrt");
        Assert.Equal(2, driveAgain.CurrentCount);
        Assert.False(driveAgain.IsDone);
    }

    [Fact]
    public async Task Adding_a_class_later_extends_the_existing_snapshot()
    {
        await SeedAsync();

        // First view with only B + A1.
        await using (var db = NewDb()) await NewService(db).GetForStudentAsync(_student);

        // Add a brand new class C with its own plan point, then register it.
        var classC = Guid.NewGuid();
        await using (var db = NewDb())
        {
            db.LicenseClasses.Add(new LicenseClass { Id = classC, Code = "C", SortOrder = 3, IsActive = true });
            db.CurriculumItems.Add(new CurriculumItem
            {
                Id = Guid.NewGuid(), ItemKey = Guid.NewGuid(), Version = 1, ValidFromUtc = DateTime.UtcNow,
                Section = "Theorie-Zusatzstoff", Title = "LKW-Stoff", SortOrder = 4, IsActive = true,
                Classes = [new CurriculumItemClass { LicenseClassId = classC }],
            });
            var student = await db.Students.Include(s => s.LicenseClasses).FirstAsync(s => s.Id == _student);
            student.LicenseClasses.Add(new StudentLicenseClass { LicenseClassId = classC, Phase = StudentPhase.Theory });
            await db.SaveChangesAsync();
        }

        await using var db2 = NewDb();
        var result = await NewService(db2).GetForStudentAsync(_student);

        var c = result.Classes.Single(x => x.Code == "C");
        // C sees the shared theory topic plus its own LKW point.
        Assert.Contains(c.Sections.SelectMany(s => s.Items), i => i.Title == "LKW-Stoff");
        Assert.Contains(c.Sections.SelectMany(s => s.Items), i => i.Title == "Grundstoff-Thema");
    }

    private static ProgressItemDto ItemByTitle(StudentProgressDto dto, string title)
        => dto.Classes.SelectMany(c => c.Sections).SelectMany(s => s.Items).First(i => i.Title == title);

    /// <summary>Audit writer that does nothing - keeps these tests focused.</summary>
    private sealed class NullAuditWriter : IAuditWriter
    {
        public Task WriteAsync(Guid? userId, string userName, string action, string entityType,
            string entityId, string? oldValuesJson = null, string? newValuesJson = null,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
