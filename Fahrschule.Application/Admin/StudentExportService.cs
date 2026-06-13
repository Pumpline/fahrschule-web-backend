using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Fahrschule.Application.Audit;
using Fahrschule.Application.LicenseClasses;
using Fahrschule.Application.Students;
using Fahrschule.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fahrschule.Application.Admin;

public interface IStudentExportService
{
    Task<(byte[] Content, string FileName)> ExportAsync(Guid studentId, Actor actor, CancellationToken ct = default);
}

/// <summary>
/// Data export for a student (KONZEPT 3.7, GDPR right to access / portability,
/// Art. 15/20): collects everything stored about the student into one readable
/// JSON file. Reuses the existing services so the export matches the screen.
/// The export itself is recorded in the audit log.
/// </summary>
public class StudentExportService(
    FahrschuleDbContext db,
    IStudentService students,
    IStudentProgressService progress,
    IStudentDocumentService documents,
    IExamService exams,
    ILessonService lessons,
    IAuditWriter auditWriter) : IStudentExportService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        // Keep umlauts readable in the exported file instead of \uXXXX escapes.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public async Task<(byte[] Content, string FileName)> ExportAsync(Guid studentId, Actor actor, CancellationToken ct = default)
    {
        var student = await students.GetByIdAsync(studentId, ct);

        var calendar = await db.CalendarEvents
            .Where(e => e.StudentId == studentId)
            .OrderBy(e => e.DateOn).ThenBy(e => e.StartTime)
            .Select(e => new
            {
                Datum = e.DateOn,
                Von = e.StartTime,
                Bis = e.EndTime,
                Art = e.Kind.ToString(),
                e.CustomTitle,
                e.Note,
            })
            .ToListAsync(ct);

        var export = new
        {
            ExportiertAm = DateTime.UtcNow,
            Hinweis = "Datenexport nach Art. 15/20 DSGVO – alle zu diesem Schüler gespeicherten Daten.",
            Stammdaten = student,
            Ausbildungsfortschritt = await progress.GetForStudentAsync(studentId, ct),
            Unterlagen = await documents.GetForStudentAsync(studentId, ct),
            Pruefungen = await exams.GetForStudentAsync(studentId, ct),
            Stunden = await lessons.GetForStudentAsync(studentId, ct),
            Termine = calendar,
        };

        var json = JsonSerializer.Serialize(export, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);

        await auditWriter.WriteAsync(actor.UserId, actor.UserName, "Daten exportiert",
            "Schüler", studentId.ToString(), cancellationToken: ct);

        var fileName = $"Datenexport_{Sanitize(student.LastName)}_{Sanitize(student.FirstName)}.json";
        return (bytes, fileName);
    }

    private static string Sanitize(string value)
    {
        var cleaned = new string(value.Where(char.IsLetterOrDigit).ToArray());
        return string.IsNullOrEmpty(cleaned) ? "Schueler" : cleaned;
    }
}
