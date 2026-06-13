using Fahrschule.Application.Audit;
using Fahrschule.Application.Common;
using Fahrschule.Application.Students;
using Fahrschule.Domain.Entities;
using Fahrschule.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fahrschule.Tests.Students;

/// <summary>
/// Tests for the "Anrechnung" preview (KONZEPT 3.3a) when adding a class:
/// unchanged shared points already done are credited, changed ones need review,
/// and class-specific points are new.
/// </summary>
public class CreditPreviewTests
{
    private readonly DbContextOptions<FahrschuleDbContext> _options =
        new DbContextOptionsBuilder<FahrschuleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    private FahrschuleDbContext NewDb() => new(_options);
    private StudentProgressService NewService(FahrschuleDbContext db) => new(db, new NullAuditWriter());

    private readonly Guid _a1 = Guid.NewGuid();
    private readonly Guid _b = Guid.NewGuid();
    private readonly Guid _student = Guid.NewGuid();
    private readonly Guid _keyUnchanged = Guid.NewGuid();
    private readonly Guid _keyChanged = Guid.NewGuid();

    /// <summary>
    /// Student is in A1 and has completed two shared theory topics. Then one of
    /// them gets a new version, and a B-specific point exists. Returns after the
    /// setup so a B credit preview can be requested.
    /// </summary>
    private async Task SeedAsync()
    {
        await using (var db = NewDb())
        {
            db.LicenseClasses.Add(new LicenseClass { Id = _a1, Code = "A1", SortOrder = 1, IsActive = true });
            db.LicenseClasses.Add(new LicenseClass { Id = _b, Code = "B", SortOrder = 2, IsActive = true });
            db.Students.Add(new Student
            {
                Id = _student, FirstName = "Max", LastName = "Muster",
                LicenseClasses = { new StudentLicenseClass { LicenseClassId = _a1, Phase = StudentPhase.Theory } },
            });
            var now = DateTime.UtcNow;
            // Two shared theory topics (apply to all classes), both version 1.
            db.CurriculumItems.Add(new CurriculumItem
            {
                Id = Guid.NewGuid(), ItemKey = _keyUnchanged, Version = 1, ValidFromUtc = now,
                Section = "Theorie-Grundstoff", Title = "GS-Unveraendert", SortOrder = 1, IsActive = true,
            });
            db.CurriculumItems.Add(new CurriculumItem
            {
                Id = Guid.NewGuid(), ItemKey = _keyChanged, Version = 1, ValidFromUtc = now,
                Section = "Theorie-Grundstoff", Title = "GS-Geaendert", SortOrder = 2, IsActive = true,
            });
            // A B-specific point.
            db.CurriculumItems.Add(new CurriculumItem
            {
                Id = Guid.NewGuid(), ItemKey = Guid.NewGuid(), Version = 1, ValidFromUtc = now,
                Section = "Theorie-Zusatzstoff", Title = "B-Spezifisch", SortOrder = 3, IsActive = true,
                Classes = [new CurriculumItemClass { LicenseClassId = _b }],
            });
            await db.SaveChangesAsync();
        }

        // Snapshot for A1 (creates the two shared progress items at version 1).
        await using (var db = NewDb()) await NewService(db).GetForStudentAsync(_student);

        // Mark both shared topics done.
        await using (var db = NewDb())
        {
            var items = await db.StudentProgressItems.Where(p => p.StudentId == _student).ToListAsync();
            foreach (var it in items) { it.IsCompleted = true; it.CompletedOn = new DateOnly(2026, 5, 1); }
            await db.SaveChangesAsync();
        }

        // The "changed" topic gets a new version (1 → 2); the snapshot keeps v1.
        await using (var db = NewDb())
        {
            var v1 = await db.CurriculumItems.FirstAsync(x => x.ItemKey == _keyChanged && x.Version == 1);
            v1.SupersededAtUtc = DateTime.UtcNow;
            db.CurriculumItems.Add(new CurriculumItem
            {
                Id = Guid.NewGuid(), ItemKey = _keyChanged, Version = 2, ValidFromUtc = DateTime.UtcNow,
                Section = "Theorie-Grundstoff", Title = "GS-Geaendert", SortOrder = 2, IsActive = true,
            });
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Preview_splits_points_into_credited_review_and_new()
    {
        await SeedAsync();
        await using var db = NewDb();

        var preview = await NewService(db).GetCreditPreviewAsync(_student, _b);

        Assert.Equal("B", preview.Code);
        Assert.Contains(preview.AlreadyCredited, i => i.Title == "GS-Unveraendert");
        Assert.Contains(preview.NeedsReview, i => i.Title == "GS-Geaendert");
        Assert.Contains(preview.NewPoints, i => i.Title == "B-Spezifisch");

        // Each point lands in exactly one bucket.
        Assert.Single(preview.AlreadyCredited);
        Assert.Single(preview.NeedsReview);
        Assert.Single(preview.NewPoints);
    }

    [Fact]
    public async Task Preview_rejects_a_class_already_assigned()
    {
        await SeedAsync();
        await using var db = NewDb();
        await Assert.ThrowsAsync<AppValidationException>(() => NewService(db).GetCreditPreviewAsync(_student, _a1));
    }

    /// <summary>Audit writer that does nothing - keeps these tests focused.</summary>
    private sealed class NullAuditWriter : IAuditWriter
    {
        public Task WriteAsync(Guid? userId, string userName, string action, string entityType,
            string entityId, string? oldValuesJson = null, string? newValuesJson = null,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
