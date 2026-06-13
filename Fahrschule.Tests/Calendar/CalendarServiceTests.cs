using Fahrschule.Application.Audit;
using Fahrschule.Application.Calendar;
using Fahrschule.Application.Common;
using Fahrschule.Application.LicenseClasses;
using Fahrschule.Contracts.Calendar;
using Fahrschule.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fahrschule.Tests.Calendar;

/// <summary>
/// Tests for the calendar service: month listing, double-booking prevention,
/// validation. In-memory provider, fresh context per call (same database).
/// </summary>
public class CalendarServiceTests
{
    private static readonly Actor TestActor = new(Guid.NewGuid(), "Test");

    private readonly DbContextOptions<FahrschuleDbContext> _options =
        new DbContextOptionsBuilder<FahrschuleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    private FahrschuleDbContext NewDb() => new(_options);
    private CalendarService NewService(FahrschuleDbContext db) => new(db, new NullAuditWriter());

    private static SaveCalendarEventRequest Req(string start, string? end, string kind = "Practice", string? custom = null)
        => new() { DateOn = new DateOnly(2026, 6, 2), StartTime = start, EndTime = end, Kind = kind, CustomTitle = custom };

    [Fact]
    public async Task Create_then_month_listing_returns_it()
    {
        await using (var db = NewDb()) await NewService(db).CreateAsync(Req("09:00", "09:45"), TestActor);

        await using var read = NewDb();
        var list = await NewService(read).GetForMonthAsync(2026, 6);
        var ev = Assert.Single(list);
        Assert.Equal("09:00", ev.StartTime);
        Assert.Equal("09:45", ev.EndTime);
        Assert.Equal("Praxis", ev.Title);

        // Another month is empty.
        Assert.Empty(await NewService(read).GetForMonthAsync(2026, 7));
    }

    [Fact]
    public async Task Overlapping_appointment_is_rejected()
    {
        await using (var db = NewDb()) await NewService(db).CreateAsync(Req("09:00", "10:00"), TestActor);

        await using var db2 = NewDb();
        await Assert.ThrowsAsync<AppValidationException>(() =>
            NewService(db2).CreateAsync(Req("09:30", "10:30"), TestActor));
    }

    [Fact]
    public async Task Adjacent_appointment_is_allowed()
    {
        await using (var db = NewDb()) await NewService(db).CreateAsync(Req("09:00", "10:00"), TestActor);
        await using (var db2 = NewDb()) await NewService(db2).CreateAsync(Req("10:00", "10:45"), TestActor); // no throw

        await using var read = NewDb();
        Assert.Equal(2, (await NewService(read).GetForMonthAsync(2026, 6)).Count);
    }

    [Fact]
    public async Task An_entry_without_end_time_never_blocks_a_slot()
    {
        await using (var db = NewDb()) await NewService(db).CreateAsync(Req("08:30", null, kind: "Exam"), TestActor);
        // A normal lesson at the same time is still allowed.
        await using (var db2 = NewDb()) await NewService(db2).CreateAsync(Req("08:30", "09:15"), TestActor); // no throw
    }

    [Fact]
    public async Task End_must_be_after_start()
    {
        await using var db = NewDb();
        await Assert.ThrowsAsync<AppValidationException>(() =>
            NewService(db).CreateAsync(Req("10:00", "09:00"), TestActor));
    }

    [Fact]
    public async Task Custom_kind_requires_a_title()
    {
        await using var db = NewDb();
        await Assert.ThrowsAsync<AppValidationException>(() =>
            NewService(db).CreateAsync(Req("09:00", "09:45", kind: "Custom"), TestActor));
    }

    [Fact]
    public async Task Update_does_not_conflict_with_itself()
    {
        Guid id;
        await using (var db = NewDb())
        {
            var dto = await NewService(db).CreateAsync(Req("09:00", "09:45"), TestActor);
            id = dto.Id;
        }

        await using var db2 = NewDb();
        var updated = await NewService(db2).UpdateAsync(id, Req("09:00", "10:15"), TestActor); // same slot, only end changed
        Assert.Equal("10:15", updated.EndTime);
    }

    [Fact]
    public async Task Delete_removes_the_appointment()
    {
        Guid id;
        await using (var db = NewDb()) id = (await NewService(db).CreateAsync(Req("09:00", "09:45"), TestActor)).Id;

        await using (var db2 = NewDb()) await NewService(db2).DeleteAsync(id, TestActor);

        await using var read = NewDb();
        Assert.Empty(await NewService(read).GetForMonthAsync(2026, 6));
    }

    /// <summary>Audit writer that does nothing - keeps these tests focused.</summary>
    private sealed class NullAuditWriter : IAuditWriter
    {
        public Task WriteAsync(Guid? userId, string userName, string action, string entityType,
            string entityId, string? oldValuesJson = null, string? newValuesJson = null,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
