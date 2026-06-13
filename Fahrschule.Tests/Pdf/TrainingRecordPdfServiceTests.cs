using Fahrschule.Application.Audit;
using Fahrschule.Application.Pdf;
using Fahrschule.Application.Settings;
using Fahrschule.Application.Students;
using Fahrschule.Domain.Entities;
using Fahrschule.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fahrschule.Tests.Pdf;

/// <summary>
/// Smoke test for the training-record PDF (KONZEPT 3.3/7): it should produce a
/// non-trivial, valid PDF for a seeded student. In-memory provider.
/// </summary>
public class TrainingRecordPdfServiceTests
{
    [Fact]
    public async Task Generate_produces_a_non_empty_pdf()
    {
        var options = new DbContextOptionsBuilder<FahrschuleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new FahrschuleDbContext(options);

        var classB = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        db.LicenseClasses.Add(new LicenseClass { Id = classB, Code = "B", SortOrder = 1, IsActive = true });
        db.Students.Add(new Student
        {
            Id = studentId, FirstName = "Lisa", LastName = "Wagner", DateOfBirth = new DateOnly(2007, 9, 2),
            LicenseClasses = { new StudentLicenseClass { LicenseClassId = classB, Phase = StudentPhase.Practice } },
        });
        db.CurriculumItems.Add(new CurriculumItem
        {
            Id = Guid.NewGuid(), ItemKey = Guid.NewGuid(), Version = 1, ValidFromUtc = DateTime.UtcNow,
            Section = "Theorie-Grundstoff", Title = "Grundstoff-Thema", SortOrder = 1, IsActive = true,
        });
        await db.SaveChangesAsync();

        var audit = new NullAuditWriter();
        var service = new TrainingRecordPdfService(
            new StudentService(db, audit),
            new StudentProgressService(db, audit),
            new ExamService(db, new SettingsService(db, audit), audit));

        var (content, fileName) = await service.GenerateAsync(studentId);

        Assert.True(content.Length > 1000, "PDF should not be trivially small.");
        // PDF files start with the "%PDF" magic bytes.
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(content, 0, 4));
        Assert.StartsWith("Ausbildungsnachweis_Wagner_Lisa", fileName);
    }

    private sealed class NullAuditWriter : IAuditWriter
    {
        public Task WriteAsync(Guid? userId, string userName, string action, string entityType,
            string entityId, string? oldValuesJson = null, string? newValuesJson = null,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
