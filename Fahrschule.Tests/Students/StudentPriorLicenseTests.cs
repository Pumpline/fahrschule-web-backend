using Fahrschule.Application.Audit;
using Fahrschule.Application.LicenseClasses;
using Fahrschule.Application.Settings;
using Fahrschule.Application.Students;
using Fahrschule.Contracts.Students;
using Fahrschule.Domain.Entities;
using Fahrschule.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fahrschule.Tests.Students;

/// <summary>
/// Vorbesitz shortens the Grundstoff (§ 4 Abs. 3 FahrschAusbO): twelve double
/// lessons for a first licence, six when the student already holds one. The
/// Zusatzstoff (§ 4 Abs. 4 + Anlage 2.8) is NOT reduced - these tests pin both
/// halves of that, because getting the second one wrong would silently let
/// students skip class-specific material.
/// </summary>
public class StudentPriorLicenseTests
{
    private static readonly Actor TestActor = new(Guid.NewGuid(), "Test");

    private readonly DbContextOptions<FahrschuleDbContext> _options =
        new DbContextOptionsBuilder<FahrschuleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    private FahrschuleDbContext NewDb() => new(_options);
    private static StudentProgressService Progress(FahrschuleDbContext db) => new(db, new NullAudit());
    private static StudentService Students(FahrschuleDbContext db)
        => new(db, new SettingsService(db, new NullAudit()), new NullAudit());

    private readonly Guid _classB = Guid.NewGuid();
    private readonly Guid _classA1 = Guid.NewGuid();
    private readonly Guid _student = Guid.NewGuid();

    /// <summary>A student training for B, plus the full twelve Grundstoff topics
    /// and one B-specific Zusatzstoff topic.</summary>
    private async Task SeedAsync()
    {
        await using var db = NewDb();

        db.LicenseClasses.Add(new LicenseClass { Id = _classB, Code = "B", SortOrder = 1, IsActive = true });
        db.LicenseClasses.Add(new LicenseClass { Id = _classA1, Code = "A1", SortOrder = 2, IsActive = true });

        db.Students.Add(new Student
        {
            Id = _student, FirstName = "Max", LastName = "Muster",
            LicenseClasses = { new StudentLicenseClass { LicenseClassId = _classB, Phase = StudentPhase.Theory } },
        });

        var now = DateTime.UtcNow;
        for (var i = 1; i <= 12; i++)
        {
            db.CurriculumItems.Add(new CurriculumItem
            {
                Id = Guid.NewGuid(), ItemKey = Guid.NewGuid(), Version = 1, ValidFromUtc = now,
                Section = "Theorie-Grundstoff", Title = $"Grundstoff {i}", SortOrder = i, IsActive = true,
                // no classes → shared, counts for every class
            });
        }
        db.CurriculumItems.Add(new CurriculumItem
        {
            Id = Guid.NewGuid(), ItemKey = Guid.NewGuid(), Version = 1, ValidFromUtc = now,
            Section = "Theorie-Zusatzstoff", Title = "B-Zusatz", SortOrder = 20, IsActive = true,
            Classes = [new CurriculumItemClass { LicenseClassId = _classB }],
        });

        await db.SaveChangesAsync();
    }

    /// <summary>Saves the Vorbesitz the way the progress tab does: one call that
    /// sets the whole block.</summary>
    private Task SetPriorClassesAsync(FahrschuleDbContext db, params Guid[] classIds)
        => Students(db).SetPriorLicenseAsync(_student,
            new SetStudentPriorLicenseRequest { LicenseClassIds = [.. classIds] }, TestActor);

    private static ProgressSectionDto BasicSection(StudentProgressDto progress)
        => progress.Classes.Single().Sections.Single(s => s.Section == "Theorie-Grundstoff");

    /// <summary>Ticks the first <paramref name="count"/> Grundstoff topics.</summary>
    private async Task CompleteBasicTopicsAsync(int count)
    {
        await using var db = NewDb();
        var service = Progress(db);
        var items = BasicSection(await service.GetForStudentAsync(_student)).Items.Take(count).ToList();
        foreach (var item in items)
        {
            await service.SetItemAsync(_student, item.Id,
                new SetProgressItemRequest { IsDone = true, CompletedOn = new DateOnly(2026, 5, 1) }, TestActor);
        }
    }

    [Fact]
    public async Task Without_a_prior_licence_all_twelve_topics_are_required()
    {
        await SeedAsync();
        await using var db = NewDb();

        var section = BasicSection(await Progress(db).GetForStudentAsync(_student));

        Assert.Equal(12, section.RequiredDoneCount);
        Assert.False(section.ReducedByPriorLicense);
    }

    [Fact]
    public async Task A_prior_licence_reduces_the_Grundstoff_to_six()
    {
        await SeedAsync();
        await using (var db = NewDb())
        {
            await SetPriorClassesAsync(db, _classA1);
        }

        await using var check = NewDb();
        var section = BasicSection(await Progress(check).GetForStudentAsync(_student));

        Assert.Equal(6, section.RequiredDoneCount);
        Assert.True(section.ReducedByPriorLicense);
        Assert.Equal(12, section.Items.Count); // the topic list itself stays whole
    }

    [Fact]
    public async Task Six_topics_finish_the_theory_when_a_prior_licence_is_recorded()
    {
        await SeedAsync();
        await using (var db = NewDb())
        {
            await SetPriorClassesAsync(db, _classA1);
        }

        await CompleteBasicTopicsAsync(6);

        // Grundstoff is satisfied, but the Zusatzstoff is NOT reduced by § 4 -
        // so the theory as a whole is still open and the Stand must not advance.
        await using (var db = NewDb())
        {
            var progress = await Progress(db).GetForStudentAsync(_student);
            Assert.Equal(6, BasicSection(progress).DoneCount);
            Assert.Equal("Theory", progress.Classes.Single().Phase);
        }

        // Tick the Zusatzstoff topic as well → theory complete, Stand advances.
        await using (var db = NewDb())
        {
            var service = Progress(db);
            var zusatz = (await service.GetForStudentAsync(_student)).Classes.Single()
                .Sections.Single(s => s.Section == "Theorie-Zusatzstoff").Items.Single();
            await service.SetItemAsync(_student, zusatz.Id,
                new SetProgressItemRequest { IsDone = true }, TestActor);
        }

        await using (var db = NewDb())
        {
            Assert.Equal("TheoryExam", (await Progress(db).GetForStudentAsync(_student)).Classes.Single().Phase);
        }
    }

    [Fact]
    public async Task Six_topics_are_not_enough_without_a_prior_licence()
    {
        await SeedAsync();
        await CompleteBasicTopicsAsync(6);

        await using var db = NewDb();
        var progress = await Progress(db).GetForStudentAsync(_student);

        Assert.Equal(6, BasicSection(progress).DoneCount);
        Assert.Equal(12, BasicSection(progress).RequiredDoneCount);
        Assert.Equal("Theory", progress.Classes.Single().Phase);
    }

    [Fact]
    public async Task Extra_topics_never_push_the_class_past_one_hundred_percent()
    {
        await SeedAsync();
        await using (var db = NewDb())
        {
            await SetPriorClassesAsync(db, _classA1);
        }

        // Eight of twelve although only six are owed.
        await CompleteBasicTopicsAsync(8);

        await using var check = NewDb();
        var progress = await Progress(check).GetForStudentAsync(_student);
        var b = progress.Classes.Single();

        Assert.Equal(6, BasicSection(progress).DoneCount); // header stays "6 von 6"
        Assert.True(b.DonePercent <= 100);
        Assert.Equal(6, b.DoneCount);          // 6 Grundstoff, Zusatzstoff still open
        Assert.Equal(7, b.TotalCount);         // 6 Grundstoff + 1 Zusatzstoff
    }

    [Fact]
    public async Task A_free_text_note_counts_as_a_prior_licence_too()
    {
        await SeedAsync();

        // A foreign licence has no class in our list - the note carries it, and
        // § 4 only asks whether a Fahrerlaubnis exists at all.
        await using (var db = NewDb())
        {
            await Students(db).SetPriorLicenseAsync(_student, new SetStudentPriorLicenseRequest
            {
                Note = "Führerschein Klasse B (Polen)",
            }, TestActor);
        }

        await using var check = NewDb();
        Assert.Equal(6, BasicSection(await Progress(check).GetForStudentAsync(_student)).RequiredDoneCount);
    }

    [Fact]
    public async Task The_instructor_can_override_the_derived_number()
    {
        await SeedAsync();
        await using (var db = NewDb())
        {
            await SetPriorClassesAsync(db, _classA1);
        }

        await using (var db = NewDb())
        {
            await Students(db).SetPriorLicenseAsync(_student, new SetStudentPriorLicenseRequest
            {
                LicenseClassIds = [_classA1],
                RequiredBasicTheoryLessonsOverride = 12,
                RequiredBasicTheoryLessonsOverrideReason = "Mofa-Prüfbescheinigung ist keine Fahrerlaubnis",
            }, TestActor);
        }

        await using var check = NewDb();
        var akteAfter = await Students(check).GetAkteAsync(_student);
        Assert.Equal(12, akteAfter.PriorLicense.RequiredBasicTheoryLessons);
        Assert.Equal(12, BasicSection(await Progress(check).GetForStudentAsync(_student)).RequiredDoneCount);
    }

    [Fact]
    public async Task A_class_being_trained_cannot_also_be_recorded_as_Vorbesitz()
    {
        await SeedAsync();
        await using var db = NewDb();

        var error = await Assert.ThrowsAsync<Fahrschule.Application.Common.AppValidationException>(
            () => SetPriorClassesAsync(db, _classB));

        Assert.Contains("ausgebildet", error.Message);
    }

    [Fact]
    public async Task Omitting_the_field_leaves_the_Vorbesitz_untouched()
    {
        await SeedAsync();
        await using (var db = NewDb()) await SetPriorClassesAsync(db, _classA1);

        // A save that does not send the list at all (null) must not wipe it -
        // otherwise any other edit would silently undo the Vorbesitz.
        await using (var db = NewDb())
        {
            var service = Students(db);
            var akte = await service.GetAkteAsync(_student);
            await service.UpdateAsync(_student, new UpdateStudentRequest
            {
                FirstName = "Max", LastName = "Muster", Version = akte.Version,
            }, TestActor);
        }

        await using var check = NewDb();
        var after = await Students(check).GetAkteAsync(_student);
        Assert.Single(after.PriorLicense.Classes);
        Assert.Equal("A1", after.PriorLicense.Classes[0].Code);
    }

    [Fact]
    public async Task Clearing_the_list_removes_the_Vorbesitz_again()
    {
        await SeedAsync();
        await using (var db = NewDb()) await SetPriorClassesAsync(db, _classA1);
        await using (var db = NewDb()) await SetPriorClassesAsync(db); // empty list

        await using var check = NewDb();
        var after = await Students(check).GetAkteAsync(_student);
        Assert.Empty(after.PriorLicense.Classes);
        Assert.False(after.PriorLicense.HasPriorLicense);
        Assert.Equal(12, after.PriorLicense.RequiredBasicTheoryLessons);
    }

    [Fact]
    public async Task A_brand_new_student_gets_the_reduced_Grundstoff_from_the_first_class_on()
    {
        // The path that has not been walked by hand yet: create a student, record
        // the Vorbesitz, THEN add the class. The plan snapshot is only built when
        // the progress is first read, so the reduced target has to survive that.
        await SeedAsync();

        Guid newStudent;
        await using (var db = NewDb())
        {
            var created = await Students(db).CreateAsync(new CreateStudentRequest
            {
                FirstName = "Nina", LastName = "Neu", DateOfBirth = new DateOnly(2006, 3, 1),
            }, TestActor);
            newStudent = created.Id;
        }

        await using (var db = NewDb())
        {
            var service = Students(db);
            await service.SetPriorLicenseAsync(newStudent,
                new SetStudentPriorLicenseRequest { LicenseClassIds = [_classA1] }, TestActor);

            // Even before any class is added, the file must state the right number
            // (there is no plan snapshot yet - it falls back to the current plan).
            var withPrior = await service.GetAkteAsync(newStudent);
            Assert.Equal(6, withPrior.PriorLicense.RequiredBasicTheoryLessons);
        }

        await using (var db = NewDb())
        {
            await Students(db).AddLicenseClassAsync(newStudent, _classB, TestActor);
        }

        await using var check = NewDb();
        var progress = await Progress(check).GetForStudentAsync(newStudent);
        var section = progress.Classes.Single().Sections.Single(s => s.Section == "Theorie-Grundstoff");

        Assert.Equal(6, section.RequiredDoneCount);
        Assert.True(section.ReducedByPriorLicense);
        Assert.Equal(12, section.Items.Count);
    }

    private sealed class NullAudit : IAuditWriter
    {
        public Task WriteAsync(Guid? userId, string userName, string action, string entityType,
            string entityId, string? oldValuesJson = null, string? newValuesJson = null,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
