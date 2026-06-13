using Fahrschule.Application.Audit;
using Fahrschule.Application.Dashboard;
using Fahrschule.Application.Settings;
using Fahrschule.Domain.Entities;
using Fahrschule.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fahrschule.Tests.Dashboard;

/// <summary>
/// Tests for the dashboard "Bald fällig" logic: documents expiring within the
/// reminder window appear, ones far in the future do not. In-memory provider.
/// </summary>
public class DashboardServiceTests
{
    [Fact]
    public async Task Upcoming_documents_respect_the_reminder_window()
    {
        var options = new DbContextOptionsBuilder<FahrschuleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new FahrschuleDbContext(options);

        var studentId = Guid.NewGuid();
        var soonDoc = Guid.NewGuid();
        var farDoc = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        db.Students.Add(new Student { Id = studentId, FirstName = "Lisa", LastName = "Wagner", CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow });
        db.DocumentCatalogItems.Add(new DocumentCatalogItem { Id = soonDoc, Name = "Fahrerlaubnis-Antrag", SortOrder = 1, IsActive = true });
        db.DocumentCatalogItems.Add(new DocumentCatalogItem { Id = farDoc, Name = "Sehtest", SortOrder = 2, IsActive = true });
        db.Set<DocumentChecklistItem>().Add(new DocumentChecklistItem
        {
            StudentId = studentId, DocumentCatalogItemId = soonDoc, IsPresent = true,
            ExpiresOn = today.AddDays(10), UpdatedAtUtc = DateTime.UtcNow,   // within 21-day window
        });
        db.Set<DocumentChecklistItem>().Add(new DocumentChecklistItem
        {
            StudentId = studentId, DocumentCatalogItemId = farDoc, IsPresent = true,
            ExpiresOn = today.AddDays(60), UpdatedAtUtc = DateTime.UtcNow,   // far in the future
        });
        await db.SaveChangesAsync();

        var audit = new NullAuditWriter();
        var service = new DashboardService(db, new SettingsService(db, audit), new AuditQueryService(db));

        var dashboard = await service.GetAsync();

        var doc = Assert.Single(dashboard.UpcomingDocuments);
        Assert.Equal("Fahrerlaubnis-Antrag", doc.DocumentName);
        Assert.Equal("Lisa Wagner", doc.StudentName);
        Assert.Equal(10, doc.DaysUntilExpiry);
    }

    [Fact]
    public async Task Open_reminders_due_soon_show_but_done_or_far_ones_do_not()
    {
        var options = new DbContextOptionsBuilder<FahrschuleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new FahrschuleDbContext(options);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        db.Reminders.Add(new Reminder { Id = Guid.NewGuid(), Title = "Antrag nachreichen", DueOn = today.AddDays(2), CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow });
        db.Reminders.Add(new Reminder { Id = Guid.NewGuid(), Title = "Schon erledigt", DueOn = today, IsDone = true, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow });
        db.Reminders.Add(new Reminder { Id = Guid.NewGuid(), Title = "Weit in der Zukunft", DueOn = today.AddDays(60), CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var audit = new NullAuditWriter();
        var service = new DashboardService(db, new SettingsService(db, audit), new AuditQueryService(db));

        var dashboard = await service.GetAsync();

        var reminder = Assert.Single(dashboard.OpenReminders);
        Assert.Equal("Antrag nachreichen", reminder.Title);
        Assert.Equal(2, reminder.DaysUntilDue);
    }

    private sealed class NullAuditWriter : IAuditWriter
    {
        public Task WriteAsync(Guid? userId, string userName, string action, string entityType,
            string entityId, string? oldValuesJson = null, string? newValuesJson = null,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
