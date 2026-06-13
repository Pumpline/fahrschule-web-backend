using Fahrschule.Application.Audit;
using Fahrschule.Application.Common;
using Fahrschule.Application.LicenseClasses;
using Fahrschule.Application.Reminders;
using Fahrschule.Contracts.Reminders;
using Fahrschule.Domain.Entities;
using Fahrschule.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fahrschule.Tests.Reminders;

/// <summary>
/// Tests for the follow-up / reminder logic ("Wiedervorlagen", KONZEPT Stufe 2):
/// validation, list filtering/ordering, mark-done, delete. In-memory provider,
/// fresh context per step (same database).
/// </summary>
public class ReminderServiceTests
{
    private static readonly Actor TestActor = new(Guid.NewGuid(), "Test");

    private readonly DbContextOptions<FahrschuleDbContext> _options =
        new DbContextOptionsBuilder<FahrschuleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    private FahrschuleDbContext NewDb() => new(_options);
    private ReminderService NewService(FahrschuleDbContext db) => new(db, new AuditWriter(db));
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public async Task Create_requires_a_title()
    {
        await using var db = NewDb();
        await Assert.ThrowsAsync<AppValidationException>(() =>
            NewService(db).CreateAsync(new SaveReminderRequest { Title = "  ", DueOn = Today }, TestActor));
    }

    [Fact]
    public async Task Create_rejects_an_unknown_student()
    {
        await using var db = NewDb();
        await Assert.ThrowsAsync<AppValidationException>(() =>
            NewService(db).CreateAsync(
                new SaveReminderRequest { Title = "Anruf", DueOn = Today, StudentId = Guid.NewGuid() }, TestActor));
    }

    [Fact]
    public async Task Create_links_a_student_and_returns_the_name()
    {
        var studentId = Guid.NewGuid();
        await using (var db = NewDb())
        {
            db.Students.Add(new Student { Id = studentId, FirstName = "Lisa", LastName = "Wagner", CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        await using (var db = NewDb())
        {
            var created = await NewService(db).CreateAsync(
                new SaveReminderRequest { Title = "Sehtest erinnern", DueOn = Today.AddDays(3), StudentId = studentId }, TestActor);
            Assert.Equal("Lisa Wagner", created.StudentName);
            Assert.False(created.IsDone);
        }
    }

    [Fact]
    public async Task List_hides_done_unless_requested_and_orders_open_first()
    {
        await using (var db = NewDb())
        {
            var service = NewService(db);
            await service.CreateAsync(new SaveReminderRequest { Title = "Später fällig", DueOn = Today.AddDays(5) }, TestActor);
            await service.CreateAsync(new SaveReminderRequest { Title = "Bald fällig", DueOn = Today.AddDays(1) }, TestActor);
            var done = await service.CreateAsync(new SaveReminderRequest { Title = "Erledigt", DueOn = Today }, TestActor);
            await service.SetDoneAsync(done.Id, true, TestActor);
        }

        await using (var db = NewDb())
        {
            var service = NewService(db);

            var open = await service.GetListAsync(includeDone: false, studentId: null);
            Assert.Equal(2, open.Count);
            Assert.Equal("Bald fällig", open[0].Title); // earliest due date first
            Assert.Equal("Später fällig", open[1].Title);

            var all = await service.GetListAsync(includeDone: true, studentId: null);
            Assert.Equal(3, all.Count);
            Assert.True(all[^1].IsDone); // done ones sort to the end
        }
    }

    [Fact]
    public async Task SetDone_toggles_completion_and_audits()
    {
        Guid id;
        await using (var db = NewDb())
            id = (await NewService(db).CreateAsync(new SaveReminderRequest { Title = "Anruf", DueOn = Today }, TestActor)).Id;

        await using (var db = NewDb())
        {
            var done = await NewService(db).SetDoneAsync(id, true, TestActor);
            Assert.True(done.IsDone);
            Assert.NotNull(done.DoneAtUtc);
        }

        await using (var db = NewDb())
        {
            var reopened = await NewService(db).SetDoneAsync(id, false, TestActor);
            Assert.False(reopened.IsDone);
            Assert.Null(reopened.DoneAtUtc);
        }

        await using (var db = NewDb())
            Assert.Contains(await db.AuditLogs.ToListAsync(), a => a.Action == "Wiedervorlage erledigt");
    }

    [Fact]
    public async Task Delete_removes_the_reminder()
    {
        Guid id;
        await using (var db = NewDb())
            id = (await NewService(db).CreateAsync(new SaveReminderRequest { Title = "Weg damit", DueOn = Today }, TestActor)).Id;

        await using (var db = NewDb()) await NewService(db).DeleteAsync(id, TestActor);

        await using (var db = NewDb())
            Assert.Empty(await NewService(db).GetListAsync(includeDone: true, studentId: null));
    }
}
