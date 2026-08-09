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
/// non-trivial, valid PDF for a seeded student. The seed deliberately covers
/// every section - theory + practice lessons, a real and a preliminary exam - so
/// a broken layout (QuestPDF throws on those) fails here. In-memory provider.
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
            JournalNumber = "2026/014",
            LicenseClasses = { new StudentLicenseClass { LicenseClassId = classB, Phase = StudentPhase.Practice } },
        });
        db.CurriculumItems.Add(new CurriculumItem
        {
            Id = Guid.NewGuid(), ItemKey = Guid.NewGuid(), Version = 1, ValidFromUtc = DateTime.UtcNow,
            Section = "Theorie-Grundstoff", Title = "Grundstoff-Thema", SortOrder = 1, IsActive = true,
        });
        await db.SaveChangesAsync();

        var audit = new NullAuditWriter();
        // A theory + a practice lesson so the record tables actually have content.
        db.Lessons.Add(new Lesson
        {
            Id = Guid.NewGuid(), StudentId = studentId, Type = LessonType.Theory, LicenseClassId = null,
            DateOn = new DateOnly(2026, 5, 2), StartTime = new TimeOnly(18, 0), DurationMinutes = 90,
            CreatedAtUtc = DateTime.UtcNow,
        });
        db.Lessons.Add(new Lesson
        {
            Id = Guid.NewGuid(), StudentId = studentId, Type = LessonType.Practice, LicenseClassId = classB,
            DateOn = new DateOnly(2026, 5, 5), StartTime = new TimeOnly(14, 0), DurationMinutes = 45,
            Note = "Überlandfahrt", CreatedAtUtc = DateTime.UtcNow,
        });
        // A real (listed) and a preliminary exam (only noted, not listed).
        db.Exams.Add(new Exam
        {
            Id = Guid.NewGuid(), StudentId = studentId, LicenseClassId = classB,
            Kind = ExamKind.Theory, IsPreliminary = false,
            DateOn = new DateOnly(2026, 5, 12), Result = ExamResult.Passed, CreatedAtUtc = DateTime.UtcNow,
        });
        db.Exams.Add(new Exam
        {
            Id = Guid.NewGuid(), StudentId = studentId, LicenseClassId = classB,
            Kind = ExamKind.Theory, IsPreliminary = true,
            DateOn = new DateOnly(2026, 5, 4), Result = ExamResult.Passed, CreatedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var settings = new SettingsService(db, audit);
        var service = new TrainingRecordPdfService(
            new StudentService(db, settings, audit),
            new LessonService(db, audit),
            new ExamService(db, settings, audit),
            settings);

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
