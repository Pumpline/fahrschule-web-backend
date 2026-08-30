using Fahrschule.Application.Audit;
using Fahrschule.Application.Common;
using Fahrschule.Application.LicenseClasses;
using Fahrschule.Application.Settings;
using Fahrschule.Application.Students;
using Fahrschule.Contracts.Students;
using Fahrschule.Domain.Entities;
using Fahrschule.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fahrschule.Tests.Students;

/// <summary>
/// Tests for exams (KONZEPT 3.4): attempt counting, the practice-needs-theory
/// rule, and the repeat lock that shortens with logged lessons. In-memory
/// provider, fresh context per call (same database).
/// </summary>
public class ExamServiceTests
{
    private static readonly Actor TestActor = new(Guid.NewGuid(), "Test");

    private readonly DbContextOptions<FahrschuleDbContext> _options =
        new DbContextOptionsBuilder<FahrschuleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    private FahrschuleDbContext NewDb() => new(_options);
    private ExamService NewService(FahrschuleDbContext db) => new(db, new SettingsService(db, new NullAuditWriter()), new NullAuditWriter());

    private readonly Guid _b = Guid.NewGuid();
    private readonly Guid _student = Guid.NewGuid();

    private async Task SeedAsync()
    {
        await using var db = NewDb();
        db.LicenseClasses.Add(new LicenseClass { Id = _b, Code = "B", SortOrder = 1, IsActive = true });
        db.Students.Add(new Student
        {
            Id = _student, FirstName = "Max", LastName = "Muster",
            LicenseClasses = { new StudentLicenseClass { LicenseClassId = _b, Phase = StudentPhase.Theory } },
        });
        await db.SaveChangesAsync();
    }

    private CreateExamRequest Req(string kind, string result, DateOnly date, bool preliminary = false) => new()
    {
        Kind = kind, IsPreliminary = preliminary, LicenseClassId = _b, DateOn = date, Result = result,
    };

    private async Task CreateAsync(CreateExamRequest req)
    {
        await using var db = NewDb();
        await NewService(db).CreateAsync(_student, req, TestActor);
    }

    [Fact]
    public async Task Real_exams_are_numbered_preliminary_are_only_noted()
    {
        await SeedAsync();
        await CreateAsync(Req("Theory", "Failed", new DateOnly(2026, 4, 1)));
        await CreateAsync(Req("Theory", "Passed", new DateOnly(2026, 4, 5), preliminary: true)); // Vorprüfung
        await CreateAsync(Req("Theory", "Passed", new DateOnly(2026, 5, 1)));

        await using var db = NewDb();
        var list = await NewService(db).GetForStudentAsync(_student);

        var real = list.Exams.Where(e => !e.IsPreliminary).OrderBy(e => e.DateOn).ToList();
        Assert.Equal(1, real[0].AttemptNumber); // failed = 1st attempt
        Assert.Equal(2, real[1].AttemptNumber); // passed = 2nd attempt
        Assert.Null(list.Exams.Single(e => e.IsPreliminary).AttemptNumber);
    }

    [Fact]
    public async Task Practice_exam_requires_passed_theory()
    {
        await SeedAsync();

        await Assert.ThrowsAsync<AppValidationException>(() =>
            CreateAsync(Req("Practice", "Planned", new DateOnly(2026, 5, 1))));

        // After the theory exam is passed, the practical exam can be entered.
        await CreateAsync(Req("Theory", "Passed", new DateOnly(2026, 4, 1)));
        await CreateAsync(Req("Practice", "Planned", new DateOnly(2026, 5, 1))); // no throw
    }

    [Fact]
    public async Task Preliminary_practice_is_allowed_without_passed_theory()
    {
        await SeedAsync();
        await CreateAsync(Req("Practice", "Passed", new DateOnly(2026, 5, 1), preliminary: true)); // no throw
    }

    [Fact]
    public async Task Failed_practice_creates_lock_that_shortens_with_lessons()
    {
        await SeedAsync();
        await CreateAsync(Req("Theory", "Passed", new DateOnly(2026, 4, 1)));
        await CreateAsync(Req("Practice", "Failed", new DateOnly(2026, 5, 1)));

        // Default lock: 2 weeks → 15.05.; not shortened yet.
        await using (var db = NewDb())
        {
            var list = await NewService(db).GetForStudentAsync(_student);
            var lock1 = Assert.Single(list.Locks);
            Assert.Equal(new DateOnly(2026, 5, 15), lock1.LockedUntil);
            Assert.False(lock1.IsShortened);
            Assert.Equal(0, lock1.LessonsSince);
        }

        // Two practice lessons after the fail → shortened to 1 week → 08.05.
        await using (var db = NewDb())
        {
            db.Lessons.Add(new Lesson { Id = Guid.NewGuid(), StudentId = _student, Type = LessonType.Practice, LicenseClassId = _b, DateOn = new DateOnly(2026, 5, 3), DurationMinutes = 90, CreatedAtUtc = DateTime.UtcNow });
            db.Lessons.Add(new Lesson { Id = Guid.NewGuid(), StudentId = _student, Type = LessonType.Practice, LicenseClassId = _b, DateOn = new DateOnly(2026, 5, 4), DurationMinutes = 90, CreatedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        await using (var db = NewDb())
        {
            var list = await NewService(db).GetForStudentAsync(_student);
            var lock1 = Assert.Single(list.Locks);
            Assert.True(lock1.IsShortened);
            Assert.Equal(2, lock1.LessonsSince);
            Assert.Equal(new DateOnly(2026, 5, 8), lock1.LockedUntil);
        }
    }

    [Fact]
    public async Task Lock_blocks_scheduling_a_repeat_before_it_ends()
    {
        await SeedAsync();
        await CreateAsync(Req("Theory", "Passed", new DateOnly(2026, 4, 1)));
        await CreateAsync(Req("Practice", "Failed", new DateOnly(2026, 5, 1))); // lock until 15.05.

        // Before the lock end → rejected.
        await Assert.ThrowsAsync<AppValidationException>(() =>
            CreateAsync(Req("Practice", "Planned", new DateOnly(2026, 5, 10))));

        // On/after the lock end → allowed.
        await CreateAsync(Req("Practice", "Planned", new DateOnly(2026, 5, 15)));
    }

    [Fact]
    public async Task Passing_resolves_the_lock()
    {
        await SeedAsync();
        await CreateAsync(Req("Theory", "Passed", new DateOnly(2026, 4, 1)));
        await CreateAsync(Req("Practice", "Failed", new DateOnly(2026, 5, 1)));
        await CreateAsync(Req("Practice", "Passed", new DateOnly(2026, 5, 20)));

        await using var db = NewDb();
        var list = await NewService(db).GetForStudentAsync(_student);
        Assert.Empty(list.Locks);
    }

    /// <summary>Audit writer that does nothing - keeps these tests focused.</summary>
    private sealed class NullAuditWriter : IAuditWriter
    {
        public Task WriteAsync(Guid? userId, string userName, string action, string entityType,
            string entityId, string? oldValuesJson = null, string? newValuesJson = null,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    // --- correcting and deleting (KONZEPT 3.4, project rule 7) ---

    private async Task<Guid> ExamIdAsync(string kind, DateOnly date)
    {
        await using var db = NewDb();
        var list = await NewService(db).GetForStudentAsync(_student);
        return list.Exams.Single(e => e.Kind == kind && e.DateOn == date).Id;
    }

    [Fact]
    public async Task Editing_an_exam_changes_date_result_and_note()
    {
        await SeedAsync();
        await CreateAsync(Req("Theory", "Planned", new DateOnly(2026, 4, 1)));
        var id = await ExamIdAsync("Theory", new DateOnly(2026, 4, 1));

        await using (var db = NewDb())
        {
            await NewService(db).UpdateAsync(_student, id, new UpdateExamRequest
            {
                DateOn = new DateOnly(2026, 4, 8), Result = "Passed", Note = "Termin verschoben",
            }, TestActor);
        }

        await using var check = NewDb();
        var exam = (await NewService(check).GetForStudentAsync(_student)).Exams.Single();
        Assert.Equal(new DateOnly(2026, 4, 8), exam.DateOn);
        Assert.Equal("Passed", exam.Result);
        Assert.Equal("Termin verschoben", exam.Note);
    }

    [Fact]
    public async Task Deleting_an_exam_renumbers_the_remaining_attempts()
    {
        await SeedAsync();
        await CreateAsync(Req("Theory", "Failed", new DateOnly(2026, 4, 1)));   // 1st attempt
        await CreateAsync(Req("Theory", "Failed", new DateOnly(2026, 5, 1)));   // 2nd attempt
        var first = await ExamIdAsync("Theory", new DateOnly(2026, 4, 1));

        await using (var db = NewDb())
        {
            await NewService(db).DeleteAsync(_student, first, TestActor);
        }

        await using var check = NewDb();
        var list = await NewService(check).GetForStudentAsync(_student);
        var remaining = Assert.Single(list.Exams);
        Assert.Equal(new DateOnly(2026, 5, 1), remaining.DateOn);
        Assert.Equal(1, remaining.AttemptNumber);   // was the 2nd attempt before
    }

    [Fact]
    public async Task Deleting_the_failed_exam_removes_its_repeat_lock()
    {
        await SeedAsync();
        await CreateAsync(Req("Theory", "Failed", new DateOnly(2026, 4, 1)));
        var failed = await ExamIdAsync("Theory", new DateOnly(2026, 4, 1));

        await using (var withLock = NewDb())
        {
            Assert.Single((await NewService(withLock).GetForStudentAsync(_student)).Locks);
        }

        await using (var db = NewDb())
        {
            await NewService(db).DeleteAsync(_student, failed, TestActor);
        }

        await using var check = NewDb();
        Assert.Empty((await NewService(check).GetForStudentAsync(_student)).Locks);
    }

    [Fact]
    public async Task A_practice_exam_keeps_its_passed_theory_exam()
    {
        await SeedAsync();
        await CreateAsync(Req("Theory", "Passed", new DateOnly(2026, 4, 1)));
        await CreateAsync(Req("Practice", "Planned", new DateOnly(2026, 5, 1)));
        var theory = await ExamIdAsync("Theory", new DateOnly(2026, 4, 1));

        // Neither deleting it ...
        await using (var db = NewDb())
        {
            await Assert.ThrowsAsync<AppValidationException>(() =>
                NewService(db).DeleteAsync(_student, theory, TestActor));
        }

        // ... nor setting it back to "not passed" is allowed.
        await using (var db = NewDb())
        {
            await Assert.ThrowsAsync<AppValidationException>(() =>
                NewService(db).UpdateAsync(_student, theory, new UpdateExamRequest
                {
                    DateOn = new DateOnly(2026, 4, 1), Result = "Failed",
                }, TestActor));
        }
    }

    [Fact]
    public async Task A_deleted_exam_no_longer_blocks_a_new_one()
    {
        await SeedAsync();
        await CreateAsync(Req("Theory", "Failed", new DateOnly(2026, 4, 1)));    // starts a 2-week lock
        var failed = await ExamIdAsync("Theory", new DateOnly(2026, 4, 1));

        // Inside the lock a repeat is refused ...
        await Assert.ThrowsAsync<AppValidationException>(() =>
            CreateAsync(Req("Theory", "Planned", new DateOnly(2026, 4, 3))));

        await using (var db = NewDb())
        {
            await NewService(db).DeleteAsync(_student, failed, TestActor);
        }

        // ... after deleting the failed attempt it works.
        await CreateAsync(Req("Theory", "Planned", new DateOnly(2026, 4, 3)));   // no throw
    }
}
