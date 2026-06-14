using Fahrschule.Application.Audit;
using Fahrschule.Application.Curriculum;
using Fahrschule.Application.LicenseClasses;
using Fahrschule.Contracts.Curriculum;
using Fahrschule.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fahrschule.Tests.Curriculum;

/// <summary>
/// Tests for the manual versioning choice when editing a curriculum item
/// (KONZEPT 3.3a): a content change either becomes a NEW version (future-only)
/// or corrects the SAME version in place (retroactive) - the editor decides.
/// </summary>
public class CurriculumItemServiceTests
{
    private static readonly Actor Actor = new(Guid.NewGuid(), "Admin");

    private readonly DbContextOptions<FahrschuleDbContext> _options =
        new DbContextOptionsBuilder<FahrschuleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    private FahrschuleDbContext NewDb() => new(_options);
    private CurriculumItemService Svc(FahrschuleDbContext db) => new(db, new AuditWriter(db));

    private async Task<CurriculumItemDto> SeedAsync()
    {
        await using var db = NewDb();
        return await Svc(db).CreateAsync(new CreateCurriculumItemRequest
        {
            Section = "Theorie-Grundstoff", Title = "Grundstoff 1", IsActive = true, SortOrder = 1,
        }, Actor);
    }

    [Fact]
    public async Task Content_change_as_new_version_keeps_the_old_one()
    {
        var created = await SeedAsync();

        await using (var db = NewDb())
        {
            await Svc(db).UpdateAsync(created.Id, new UpdateCurriculumItemRequest
            {
                Title = "Grundstoff 1 (neu)", IsActive = true, SortOrder = 1,
                AsNewVersion = true, RowVersion = created.RowVersion,
            }, Actor);
        }

        await using (var db = NewDb())
        {
            Assert.Equal(2, await db.CurriculumItems.CountAsync());           // old + new kept
            var current = Assert.Single(await db.CurriculumItems.Where(x => x.SupersededAtUtc == null).ToListAsync());
            Assert.Equal(2, current.Version);
            Assert.Equal("Grundstoff 1 (neu)", current.Title);
        }
    }

    [Fact]
    public async Task Content_change_in_place_corrects_the_same_version()
    {
        var created = await SeedAsync();

        await using (var db = NewDb())
        {
            await Svc(db).UpdateAsync(created.Id, new UpdateCurriculumItemRequest
            {
                Title = "Grundstoff 1 korrigiert", IsActive = true, SortOrder = 1,
                AsNewVersion = false, RowVersion = created.RowVersion,
            }, Actor);
        }

        await using (var db = NewDb())
        {
            var only = Assert.Single(await db.CurriculumItems.ToListAsync());  // no new row
            Assert.Equal(1, only.Version);
            Assert.Null(only.SupersededAtUtc);
            Assert.Equal("Grundstoff 1 korrigiert", only.Title);
        }
    }
}
