using Fahrschule.Application.Audit;
using Fahrschule.Application.LicenseClasses;
using Fahrschule.Application.Students;
using Fahrschule.Application.Theory;
using Fahrschule.Contracts.Theory;
using Fahrschule.Domain.Entities;
using Fahrschule.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fahrschule.Tests.Theory;

/// <summary>
/// Tests for the theory attendance shortcut (KONZEPT Stufe 2): ticking one topic
/// for several students at once. Nothing is stored as an attendance list - only
/// the students' theory progress is updated. In-memory provider.
/// </summary>
public class TheoryAttendanceServiceTests
{
    private static readonly Actor TestActor = new(Guid.NewGuid(), "Test");
    private static readonly DateOnly Date = new(2026, 6, 1);

    private readonly DbContextOptions<FahrschuleDbContext> _options =
        new DbContextOptionsBuilder<FahrschuleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    private FahrschuleDbContext NewDb() => new(_options);

    private TheoryAttendanceService NewService(FahrschuleDbContext db)
    {
        var audit = new AuditWriter(db);
        return new TheoryAttendanceService(db, new StudentProgressService(db, audit), audit);
    }

    private readonly Guid _withClass = Guid.NewGuid();   // has class B → topic applies
    private readonly Guid _noClass = Guid.NewGuid();     // no class → topic not in plan
    private readonly Guid _topicId = Guid.NewGuid();
    private readonly Guid _topicKey = Guid.NewGuid();

    private async Task SeedAsync()
    {
        await using var db = NewDb();
        var classId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        db.LicenseClasses.Add(new LicenseClass { Id = classId, Code = "B", Description = "Pkw", SortOrder = 1, MinimumAge = 18, IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now });
        db.Students.Add(new Student
        {
            Id = _withClass, FirstName = "Lisa", LastName = "Wagner", DateOfBirth = new DateOnly(2006, 5, 1),
            CreatedAtUtc = now, UpdatedAtUtc = now,
            LicenseClasses = [new StudentLicenseClass { LicenseClassId = classId, Phase = StudentPhase.Theory, AddedAtUtc = now }],
        });
        db.Students.Add(new Student { Id = _noClass, FirstName = "Tom", LastName = "Ohne", DateOfBirth = new DateOnly(2005, 1, 1), CreatedAtUtc = now, UpdatedAtUtc = now });
        db.CurriculumItems.Add(new CurriculumItem
        {
            Id = _topicId, ItemKey = _topicKey, Version = 1, ValidFromUtc = now,
            Section = "Theorie-Grundstoff", Title = "Vorfahrt", RequiredCount = null,
            IsActive = true, SortOrder = 1, CreatedAtUtc = now, UpdatedAtUtc = now,
        });
        await db.SaveChangesAsync();
    }

    private TickTheoryRequest Request(params Guid[] studentIds)
        => new() { DateOn = Date, CurriculumItemId = _topicId, StudentIds = [.. studentIds] };

    private async Task<bool> TopicDoneAsync(Guid studentId)
    {
        await using var db = NewDb();
        var item = await db.StudentProgressItems
            .FirstOrDefaultAsync(p => p.StudentId == studentId && p.CurriculumItemKey == _topicKey);
        return item?.IsCompleted ?? false;
    }

    [Fact]
    public async Task Topics_lists_the_current_theory_topic()
    {
        await SeedAsync();
        await using var db = NewDb();
        var topic = Assert.Single(await NewService(db).GetTopicsAsync());
        Assert.Equal("Vorfahrt", topic.Title);
    }

    [Fact]
    public async Task Ticking_marks_the_topic_done_on_the_given_date()
    {
        await SeedAsync();

        TheoryTickResultDto result;
        await using (var db = NewDb())
            result = await NewService(db).TickAsync(Request(_withClass), TestActor);

        Assert.Equal(1, result.Ticked);
        Assert.Equal(0, result.AlreadyDone);
        Assert.True(await TopicDoneAsync(_withClass));

        await using (var db = NewDb())
        {
            var item = await db.StudentProgressItems.FirstAsync(p => p.StudentId == _withClass && p.CurriculumItemKey == _topicKey);
            Assert.Equal(Date, item.CompletedOn);
        }
    }

    [Fact]
    public async Task Ticking_again_counts_as_already_done()
    {
        await SeedAsync();
        await using (var db = NewDb()) await NewService(db).TickAsync(Request(_withClass), TestActor);

        await using (var db = NewDb())
        {
            var result = await NewService(db).TickAsync(Request(_withClass), TestActor);
            Assert.Equal(0, result.Ticked);
            Assert.Equal(1, result.AlreadyDone);
        }
    }

    [Fact]
    public async Task Student_without_the_topic_in_plan_is_not_applicable()
    {
        await SeedAsync();

        await using var db = NewDb();
        var result = await NewService(db).TickAsync(Request(_withClass, _noClass), TestActor);

        Assert.Equal(1, result.Ticked);          // Lisa (has class B)
        Assert.Equal(1, result.NotApplicable);   // Tom (no class → topic not in plan)
    }

    [Fact]
    public async Task Empty_student_list_is_rejected()
    {
        await SeedAsync();
        await using var db = NewDb();
        await Assert.ThrowsAsync<Fahrschule.Application.Common.AppValidationException>(() =>
            NewService(db).TickAsync(Request(), TestActor));
    }
}
