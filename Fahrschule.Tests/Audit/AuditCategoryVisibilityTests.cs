using Fahrschule.Application.Audit;
using Fahrschule.Application.LicenseClasses;
using Fahrschule.Contracts.Admin;
using Fahrschule.Domain.Entities;
using Fahrschule.Infrastructure.Identity;
using Fahrschule.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fahrschule.Tests.Audit;

/// <summary>
/// Tests for the categorised, role-filtered change log (KONZEPT 1/4): the writer
/// derives a category from the action/entity, each role only sees the categories
/// configured for it, the initiator's CURRENT name is shown, and student entries
/// carry the student's id + name for a link.
/// </summary>
public class AuditCategoryVisibilityTests
{
    private readonly DbContextOptions<FahrschuleDbContext> _options =
        new DbContextOptionsBuilder<FahrschuleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    private FahrschuleDbContext NewDb() => new(_options);
    private static AuditQueryService Query(FahrschuleDbContext db) =>
        new(db, new AuditVisibilityService(db, new AuditWriter(db)));

    [Fact]
    public async Task Writer_derives_the_category_from_action_and_entity()
    {
        await using var db = NewDb();
        var writer = new AuditWriter(db);
        await writer.WriteAsync(Guid.NewGuid(), "A", "PasswortGeändert", "Benutzer", "x");
        await writer.WriteAsync(Guid.NewGuid(), "A", "Geändert", "Schüler", Guid.NewGuid().ToString());

        var byEntity = await db.AuditLogs.ToListAsync();
        Assert.Equal(AuditCategory.Security, byEntity.Single(a => a.EntityType == "Benutzer").Category);
        Assert.Equal(AuditCategory.Students, byEntity.Single(a => a.EntityType == "Schüler").Category);
    }

    [Fact]
    public async Task Instructor_does_not_see_the_security_category()
    {
        await using (var db = NewDb())
        {
            var writer = new AuditWriter(db);
            await writer.WriteAsync(Guid.NewGuid(), "A", "PasswortGeändert", "Benutzer", "x"); // security
            await writer.WriteAsync(Guid.NewGuid(), "A", "Geändert", "Schüler", Guid.NewGuid().ToString()); // students
        }

        await using (var db = NewDb())
        {
            var page = await Query(db).GetListAsync(["Fahrlehrer"], null, null, 1, 20);
            Assert.Equal(1, page.Total);
            Assert.Equal("Schüler", page.Items.Single().EntityType);
            // The filter chips offered to the instructor exclude security/users.
            Assert.DoesNotContain(page.Categories, c => c.Key == AuditCategory.Security);
            Assert.Contains(page.Categories, c => c.Key == AuditCategory.Students);
        }

        await using (var db = NewDb())
        {
            // The Admin sees both entries.
            var page = await Query(db).GetListAsync(["Admin"], null, null, 1, 20);
            Assert.Equal(2, page.Total);
        }
    }

    [Fact]
    public async Task Initiators_current_name_is_shown()
    {
        var userId = Guid.NewGuid();
        await using (var db = NewDb())
        {
            db.Users.Add(new ApplicationUser { Id = userId, UserName = "u@x", DisplayName = "Alt" });
            await db.SaveChangesAsync();
            await new AuditWriter(db).WriteAsync(userId, "Alt", "Geändert", "Schüler", Guid.NewGuid().ToString());
        }

        await using (var db = NewDb())
        {
            var user = await db.Users.FirstAsync(u => u.Id == userId);
            user.DisplayName = "Neu";
            await db.SaveChangesAsync();
        }

        await using (var db = NewDb())
        {
            var page = await Query(db).GetListAsync(["Admin"], null, null, 1, 20);
            Assert.Equal("Neu", page.Items.Single().UserName);
        }
    }

    [Fact]
    public async Task Student_entries_carry_the_students_id_and_name()
    {
        var studentId = Guid.NewGuid();
        await using (var db = NewDb())
        {
            db.Students.Add(new Student
            {
                Id = studentId, FirstName = "Lisa", LastName = "Wagner",
                CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
            // Progress entry: EntityId is "studentId/title" - the leading id must still resolve.
            await new AuditWriter(db).WriteAsync(Guid.NewGuid(), "A", "Abgehakt",
                "Ausbildungsfortschritt", $"{studentId}/Grundstoff 1");
        }

        await using (var db = NewDb())
        {
            var item = (await Query(db).GetListAsync(["Admin"], null, null, 1, 20)).Items.Single();
            Assert.Equal(studentId, item.StudentId);
            Assert.Equal("Lisa Wagner", item.StudentName);
        }
    }

    [Fact]
    public async Task Detail_shows_the_specifics_of_an_action()
    {
        var studentId = Guid.NewGuid();
        await using (var db = NewDb())
        {
            db.Students.Add(new Student
            {
                Id = studentId, FirstName = "Lisa", LastName = "Wagner",
                CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
            var w = new AuditWriter(db);
            // viewed field, ticked plan point, and a change that only touches the e-mail
            await w.WriteAsync(Guid.NewGuid(), "A", "Stammdaten angesehen", "Schüler",
                studentId.ToString(), newValuesJson: "{\"Feld\":\"E-Mail\"}");
            await w.WriteAsync(Guid.NewGuid(), "A", "Abgehakt", "Ausbildungsfortschritt", $"{studentId}/Überlandfahrt");
            await w.WriteAsync(Guid.NewGuid(), "A", "Geändert", "Schüler", studentId.ToString(),
                oldValuesJson: "{\"Email\":\"a@x\",\"Phone\":\"1\"}", newValuesJson: "{\"Email\":\"b@x\",\"Phone\":\"1\"}");
            // class added (new only) and removed (old only) + a phase change
            await w.WriteAsync(Guid.NewGuid(), "A", "Geändert", "Schüler", studentId.ToString(),
                newValuesJson: "{\"KlasseHinzugefügt\":\"B\"}");
            await w.WriteAsync(Guid.NewGuid(), "A", "Geändert", "Schüler", studentId.ToString(),
                oldValuesJson: "{\"KlasseEntfernt\":\"A1\"}");
            await w.WriteAsync(Guid.NewGuid(), "A", "Prüfung eingetragen", "Prüfung", studentId.ToString(),
                newValuesJson: "{\"Art\":\"Praxisprüfung\",\"Klasse\":\"B\",\"Datum\":\"05.06.2026\",\"Ergebnis\":\"bestanden\"}");
        }

        await using (var db = NewDb())
        {
            var items = (await Query(db).GetListAsync(["Admin"], null, null, 1, 20)).Items;
            Assert.Equal("E-Mail", items.Single(i => i.Action == "Stammdaten angesehen").Detail);
            Assert.Equal("Überlandfahrt", items.Single(i => i.Action == "Abgehakt").Detail);
            // only the e-mail changed → just that field's label, never the values
            Assert.Equal("E-Mail", items.Single(i => i.Action == "Geändert" && i.Detail == "E-Mail").Detail);
            Assert.Contains(items, i => i.Detail == "Klasse B hinzugefügt");
            Assert.Contains(items, i => i.Detail == "Klasse A1 entfernt");
            Assert.Equal("Praxisprüfung, Klasse B, bestanden", items.Single(i => i.Action == "Prüfung eingetragen").Detail);
        }
    }

    [Fact]
    public async Task Saving_visibility_restricts_what_a_role_sees()
    {
        await using (var db = NewDb())
        {
            await new AuditWriter(db).WriteAsync(Guid.NewGuid(), "A", "Termin angelegt", "Termin", "01.06.2026");
        }

        await using (var db = NewDb())
        {
            // Take the calendar category away from Verwaltung.
            var service = new AuditVisibilityService(db, new AuditWriter(db));
            await service.SaveConfigAsync(new AuditVisibilityDto
            {
                Roles =
                [
                    new() { Role = "Verwaltung", Categories = [AuditCategory.Students, AuditCategory.Training] },
                    new() { Role = "Fahrlehrer", Categories = [AuditCategory.Calendar] },
                ],
            }, new Actor(Guid.NewGuid(), "Admin"));
        }

        await using (var db = NewDb())
        {
            Assert.Equal(0, (await Query(db).GetListAsync(["Verwaltung"], null, null, 1, 20)).Total);
            Assert.Equal(1, (await Query(db).GetListAsync(["Fahrlehrer"], null, null, 1, 20)).Total);
        }
    }
}
