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
/// Tests for theory attendance lists (KONZEPT Stufe 2, integrated variant):
/// marking a student present at a theory double lesson ticks the topic in their
/// progress; removing/deleting undoes exactly that. In-memory provider.
/// </summary>
public class TheorySessionServiceTests
{
    private static readonly Actor TestActor = new(Guid.NewGuid(), "Test");
    private static readonly DateOnly SessionDate = new(2026, 6, 1);

    private readonly DbContextOptions<FahrschuleDbContext> _options =
        new DbContextOptionsBuilder<FahrschuleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    private FahrschuleDbContext NewDb() => new(_options);

    private TheorySessionService NewService(FahrschuleDbContext db)
    {
        var audit = new AuditWriter(db);
        return new TheorySessionService(db, new StudentProgressService(db, audit), audit);
    }

    private readonly Guid _studentId = Guid.NewGuid();
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
            Id = _studentId, FirstName = "Lisa", LastName = "Wagner",
            DateOfBirth = new DateOnly(2006, 5, 1), CreatedAtUtc = now, UpdatedAtUtc = now,
            LicenseClasses = [new StudentLicenseClass { LicenseClassId = classId, Phase = StudentPhase.Theory, AddedAtUtc = now }],
        });
        // Current theory topic, simple check-off, applies to all classes.
        db.CurriculumItems.Add(new CurriculumItem
        {
            Id = _topicId, ItemKey = _topicKey, Version = 1, ValidFromUtc = now,
            Section = "Theorie-Grundstoff", Title = "Vorfahrt und Verkehrsregelungen",
            RequiredCount = null, IsActive = true, SortOrder = 1, CreatedAtUtc = now, UpdatedAtUtc = now,
        });
        await db.SaveChangesAsync();
    }

    private async Task<bool> TopicDoneAsync()
    {
        await using var db = NewDb();
        var item = await db.StudentProgressItems
            .FirstOrDefaultAsync(p => p.StudentId == _studentId && p.CurriculumItemKey == _topicKey);
        return item?.IsCompleted ?? false;
    }

    private CreateTheorySessionRequest CreateRequest(params Guid[] studentIds) => new()
    {
        DateOn = SessionDate, DurationMinutes = 90, CurriculumItemId = _topicId, StudentIds = [.. studentIds],
    };

    [Fact]
    public async Task Topics_lists_the_current_theory_topic()
    {
        await SeedAsync();
        await using var db = NewDb();
        var topics = await NewService(db).GetTopicsAsync();
        var topic = Assert.Single(topics);
        Assert.Equal(_topicId, topic.Id);
        Assert.Equal("Vorfahrt und Verkehrsregelungen", topic.Title);
    }

    [Fact]
    public async Task Creating_a_session_with_an_attendee_ticks_the_topic()
    {
        await SeedAsync();

        TheorySessionDetailDto detail;
        await using (var db = NewDb())
            detail = await NewService(db).CreateAsync(CreateRequest(_studentId), TestActor);

        var attendee = Assert.Single(detail.Attendees);
        Assert.Equal("Lisa Wagner", attendee.FullName);
        Assert.True(attendee.CountedProgress);
        Assert.True(await TopicDoneAsync());

        // The completion is dated on the session day.
        await using (var db = NewDb())
        {
            var item = await db.StudentProgressItems.FirstAsync(p => p.CurriculumItemKey == _topicKey);
            Assert.Equal(SessionDate, item.CompletedOn);
        }
    }

    [Fact]
    public async Task Removing_an_attendee_undoes_the_tick()
    {
        await SeedAsync();
        Guid sessionId;
        await using (var db = NewDb())
            sessionId = (await NewService(db).CreateAsync(CreateRequest(_studentId), TestActor)).Id;

        Assert.True(await TopicDoneAsync());

        await using (var db = NewDb())
        {
            var detail = await NewService(db).RemoveAttendeeAsync(sessionId, _studentId, TestActor);
            Assert.Empty(detail.Attendees);
        }

        Assert.False(await TopicDoneAsync());
    }

    [Fact]
    public async Task Deleting_a_session_undoes_all_ticks_and_removes_it()
    {
        await SeedAsync();
        Guid sessionId;
        await using (var db = NewDb())
            sessionId = (await NewService(db).CreateAsync(CreateRequest(_studentId), TestActor)).Id;

        await using (var db = NewDb()) await NewService(db).DeleteAsync(sessionId, TestActor);

        Assert.False(await TopicDoneAsync());
        await using (var db = NewDb())
        {
            Assert.False(await db.TheorySessions.AnyAsync(s => s.Id == sessionId));
            Assert.False(await db.TheoryAttendances.AnyAsync(a => a.TheorySessionId == sessionId));
        }
    }

    [Fact]
    public async Task Already_completed_topic_is_not_re_ticked_and_not_reverted()
    {
        await SeedAsync();

        // The student completed the topic manually beforehand (different date).
        await using (var db = NewDb())
        {
            await NewService(db).GetTopicsAsync(); // no-op; ensures db usable
        }
        await using (var db = NewDb())
        {
            // Force the snapshot, then mark done manually on another date.
            await new StudentProgressService(db, new AuditWriter(db)).GetForStudentAsync(_studentId);
            var item = await db.StudentProgressItems.FirstAsync(p => p.CurriculumItemKey == _topicKey);
            item.IsCompleted = true;
            item.CompletedOn = new DateOnly(2026, 5, 20);
            await db.SaveChangesAsync();
        }

        Guid sessionId;
        await using (var db = NewDb())
        {
            var detail = await NewService(db).CreateAsync(CreateRequest(_studentId), TestActor);
            Assert.False(Assert.Single(detail.Attendees).CountedProgress); // was already done
            sessionId = detail.Id;
        }

        // Removing the attendance must NOT undo the manual completion.
        await using (var db = NewDb()) await NewService(db).RemoveAttendeeAsync(sessionId, _studentId, TestActor);
        Assert.True(await TopicDoneAsync());
    }
}
