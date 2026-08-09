using Fahrschule.Application.Audit;
using Fahrschule.Application.Common;
using Fahrschule.Application.LicenseClasses;
using Fahrschule.Application.Students;
using Fahrschule.Contracts.Students;
using Fahrschule.Domain.Entities;
using Fahrschule.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fahrschule.Tests.Students;

/// <summary>
/// Tests for the lazy, audited master-data handling (KONZEPT 3.1 / DSGVO): the
/// Akte carries only field status (no values), revealing a field is audited, and
/// saving only overwrites the fields the client actually loaded.
/// </summary>
public class StudentMasterDataTests
{
    private static readonly Actor TestActor = new(Guid.NewGuid(), "Test");

    private readonly DbContextOptions<FahrschuleDbContext> _options =
        new DbContextOptionsBuilder<FahrschuleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    private FahrschuleDbContext NewDb() => new(_options);
    private StudentService NewService(FahrschuleDbContext db) => new(db, new AuditWriter(db));

    private readonly Guid _id = Guid.NewGuid();

    private async Task SeedAsync()
    {
        await using var db = NewDb();
        db.Students.Add(new Student
        {
            Id = _id, FirstName = "Lisa", LastName = "Wagner", JournalNumber = "2026/014",
            DateOfBirth = new DateOnly(2006, 5, 1),
            Email = "lisa@example.com", Phone = null, Address = "Hauptstr. 1", Notes = null,
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Akte_reports_which_fields_are_filled_without_values()
    {
        await SeedAsync();
        await using var db = NewDb();
        var akte = await NewService(db).GetAkteAsync(_id);

        bool Has(string key) => akte.Fields.First(f => f.Key == key).HasValue;
        Assert.True(Has("dateOfBirth"));
        Assert.True(Has("email"));
        Assert.False(Has("phone"));
        Assert.True(Has("address"));
        Assert.False(Has("notes"));
    }

    [Fact]
    public async Task Revealing_a_field_returns_the_value_and_is_audited()
    {
        await SeedAsync();
        await using (var db = NewDb())
        {
            var field = await NewService(db).GetFieldAsync(_id, "email", TestActor);
            Assert.Equal("lisa@example.com", field.Value);
        }

        await using (var db = NewDb())
        {
            var entry = await db.AuditLogs.SingleAsync(a => a.Action == "Stammdaten angesehen");
            Assert.Equal("Schüler", entry.EntityType);
            Assert.Contains("E-Mail", entry.NewValuesJson);
        }
    }

    [Fact]
    public async Task Update_without_a_real_change_writes_no_audit_entry()
    {
        await SeedAsync();

        uint version;
        await using (var db = NewDb()) version = (await NewService(db).GetAkteAsync(_id)).Version;

        // Resend exactly the seeded values (all fields revealed) → nothing changes.
        await using (var db = NewDb())
        {
            await NewService(db).UpdateAsync(_id, new UpdateStudentRequest
            {
                FirstName = "Lisa", LastName = "Wagner", JournalNumber = "2026/014", Version = version,
                DateOfBirth = new DateOnly(2006, 5, 1), Email = "lisa@example.com",
                Phone = null, Address = "Hauptstr. 1", Notes = null,
                EditableFields = ["dateOfBirth", "email", "phone", "address", "notes"],
            }, TestActor);
        }

        await using (var db = NewDb())
        {
            Assert.False(await db.AuditLogs.AnyAsync(a => a.Action == "Geändert" && a.EntityType == "Schüler"));
        }
    }

    [Fact]
    public async Task Journal_number_is_visible_in_the_akte_like_the_name()
    {
        await SeedAsync();
        await using var db = NewDb();
        var akte = await NewService(db).GetAkteAsync(_id);

        // It is a file number the office needs at a glance, so it is NOT one of
        // the hidden fields behind the reveal button - it comes with the Akte.
        Assert.Equal("2026/014", akte.JournalNumber);
        Assert.DoesNotContain(akte.Fields, f => f.Key == "journalNumber");
    }

    [Fact]
    public async Task The_same_journal_number_cannot_be_used_twice()
    {
        await SeedAsync();
        await using var db = NewDb();

        // Different spelling, same number - the check ignores case and spaces.
        var error = await Assert.ThrowsAsync<AppValidationException>(() =>
            NewService(db).CreateAsync(new CreateStudentRequest
            {
                FirstName = "Tom", LastName = "Berger", JournalNumber = " 2026/014 ",
            }, TestActor));

        Assert.Contains("bereits", error.Message);
    }

    [Fact]
    public async Task A_student_may_keep_their_own_journal_number_when_saving()
    {
        await SeedAsync();

        uint version;
        await using (var db = NewDb()) version = (await NewService(db).GetAkteAsync(_id)).Version;

        await using (var db = NewDb())
        {
            var akte = await NewService(db).UpdateAsync(_id, new UpdateStudentRequest
            {
                FirstName = "Lisa", LastName = "Wagner", JournalNumber = "2026/014", Version = version,
            }, TestActor);

            Assert.Equal("2026/014", akte.JournalNumber);
        }
    }

    [Fact]
    public async Task Update_only_changes_fields_listed_as_editable()
    {
        await SeedAsync();

        uint version;
        await using (var db = NewDb()) version = (await NewService(db).GetAkteAsync(_id)).Version;

        // Save the phone (newly filled). Email is sent as null but NOT marked
        // editable - it must stay untouched.
        await using (var db = NewDb())
        {
            await NewService(db).UpdateAsync(_id, new UpdateStudentRequest
            {
                FirstName = "Lisa", LastName = "Wagner", JournalNumber = "2026/014", Version = version,
                Phone = "0123 456", Email = null,
                EditableFields = ["phone"],
            }, TestActor);
        }

        await using (var db = NewDb())
        {
            var service = NewService(db);
            Assert.Equal("0123 456", (await service.GetFieldAsync(_id, "phone", TestActor)).Value);
            Assert.Equal("lisa@example.com", (await service.GetFieldAsync(_id, "email", TestActor)).Value);
        }
    }
}
