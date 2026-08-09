using Fahrschule.Application.Admin;
using Fahrschule.Application.Audit;
using Fahrschule.Application.LicenseClasses;
using Fahrschule.Application.Settings;
using Fahrschule.Application.Students;
using Fahrschule.Domain.Entities;
using Fahrschule.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fahrschule.Tests.Admin;

/// <summary>
/// Tests for the GDPR admin functions (KONZEPT 3.7): the "marked for deletion"
/// list with restore, the audit-log paging, and the data export. In-memory
/// provider, fresh context per call (same database).
/// </summary>
public class DsgvoServiceTests
{
    private static readonly Actor TestActor = new(Guid.NewGuid(), "Test");

    private readonly DbContextOptions<FahrschuleDbContext> _options =
        new DbContextOptionsBuilder<FahrschuleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    private FahrschuleDbContext NewDb() => new(_options);

    private readonly Guid _student = Guid.NewGuid();

    private async Task SeedStudentAsync()
    {
        await using var db = NewDb();
        db.Students.Add(new Student
        {
            Id = _student, FirstName = "Lisa", LastName = "Wagner",
            DateOfBirth = new DateOnly(2007, 9, 2), CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Soft_deleted_student_appears_in_the_list_and_can_be_restored()
    {
        await SeedStudentAsync();

        await using (var db = NewDb()) await new StudentService(db, new SettingsService(db, new NullAuditWriter()), new NullAuditWriter()).DeleteAsync(_student, TestActor);

        await using (var db = NewDb())
        {
            var deleted = await new StudentService(db, new SettingsService(db, new NullAuditWriter()), new NullAuditWriter()).GetDeletedAsync();
            Assert.Single(deleted);
            Assert.Equal("Lisa Wagner", deleted[0].FullName);
        }

        await using (var db = NewDb()) await new StudentService(db, new SettingsService(db, new NullAuditWriter()), new NullAuditWriter()).RestoreAsync(_student, TestActor);

        await using (var db = NewDb())
        {
            var service = new StudentService(db, new SettingsService(db, new NullAuditWriter()), new NullAuditWriter());
            Assert.Empty(await service.GetDeletedAsync());
            // Restored → visible again to normal queries.
            var detail = await service.GetByIdAsync(_student);
            Assert.Equal("Wagner", detail.LastName);
        }
    }

    [Fact]
    public async Task Audit_list_is_paginated_newest_first()
    {
        await using (var db = NewDb())
        {
            for (var i = 0; i < 5; i++)
            {
                db.AuditLogs.Add(new AuditLog
                {
                    TimestampUtc = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(i),
                    UserName = "Test", Action = "Geändert", EntityType = "Schüler", EntityId = $"nr-{i}",
                    Category = AuditCategory.Students,
                });
            }
            await db.SaveChangesAsync();
        }

        await using var read = NewDb();
        var auditQuery = new AuditQueryService(read, new AuditVisibilityService(read, new NullAuditWriter()));
        var page = await auditQuery.GetListAsync(["Admin"], search: null, category: null, page: 1, pageSize: 2);

        Assert.Equal(5, page.Total);
        Assert.Equal(2, page.Items.Count);
        // Newest first: nr-4 then nr-3.
        Assert.Equal("nr-4", page.Items[0].EntityId);
        Assert.Equal("nr-3", page.Items[1].EntityId);
    }

    [Fact]
    public async Task Export_produces_json_with_the_student_data()
    {
        await SeedStudentAsync();
        await using var db = NewDb();
        var audit = new NullAuditWriter();
        var service = new StudentExportService(
            db,
            new StudentService(db, new SettingsService(db, audit), audit),
            new StudentProgressService(db, audit),
            new StudentDocumentService(db, new SettingsService(db, audit), audit),
            new ExamService(db, new SettingsService(db, audit), audit),
            new LessonService(db, audit),
            audit);

        var (content, fileName) = await service.ExportAsync(_student, TestActor);

        var json = System.Text.Encoding.UTF8.GetString(content);
        Assert.Contains("Wagner", json);
        Assert.Contains("Ausbildungsfortschritt", json);
        Assert.StartsWith("Datenexport_Wagner_Lisa", fileName);
    }

    private sealed class NullAuditWriter : IAuditWriter
    {
        public Task WriteAsync(Guid? userId, string userName, string action, string entityType,
            string entityId, string? oldValuesJson = null, string? newValuesJson = null,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
