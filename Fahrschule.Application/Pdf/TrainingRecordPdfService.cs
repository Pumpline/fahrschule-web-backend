using Fahrschule.Application.Settings;
using Fahrschule.Application.Students;
using Fahrschule.Contracts.Settings;
using Fahrschule.Contracts.Students;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Fahrschule.Application.Pdf;

public interface ITrainingRecordPdfService
{
    Task<(byte[] Content, string FileName)> GenerateAsync(Guid studentId, CancellationToken ct = default);
}

/// <summary>
/// Generates the printable Ausbildungsnachweis (training record, KONZEPT 3.3/7)
/// for a student: the progress per licence class plus the exams. Built with
/// QuestPDF (Community licence - allowed for this small family business).
///
/// It reuses the existing services so the PDF always shows the same numbers as
/// the screen. Layout lives in <see cref="TrainingRecordDocument"/>.
/// </summary>
public class TrainingRecordPdfService(
    IStudentService students,
    IStudentProgressService progress,
    IExamService exams,
    ISettingsService settings) : ITrainingRecordPdfService
{
    static TrainingRecordPdfService()
    {
        // Free for companies below the revenue threshold (KONZEPT 7).
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public async Task<(byte[] Content, string FileName)> GenerateAsync(Guid studentId, CancellationToken ct = default)
    {
        var student = await students.GetByIdAsync(studentId, ct);
        var prog = await progress.GetForStudentAsync(studentId, ct);
        var examList = await exams.GetForStudentAsync(studentId, ct);
        var appSettings = await settings.GetAsync(ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var bytes = new TrainingRecordDocument(student, prog, examList, appSettings, today).GeneratePdf();
        var fileName = $"Ausbildungsnachweis_{Sanitize(student.LastName)}_{Sanitize(student.FirstName)}.pdf";
        return (bytes, fileName);
    }

    private static string Sanitize(string value)
    {
        var cleaned = new string(value.Where(c => char.IsLetterOrDigit(c)).ToArray());
        return string.IsNullOrEmpty(cleaned) ? "Schueler" : cleaned;
    }
}

/// <summary>The QuestPDF layout of the training record (German - it is printed).</summary>
public class TrainingRecordDocument(
    StudentDetailDto student,
    StudentProgressDto progress,
    ExamListDto exams,
    AppSettingsDto settings,
    DateOnly generatedOn) : IDocument
{
    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(40);
            page.DefaultTextStyle(x => x.FontSize(10).FontColor(Colors.Black));

            page.Header().Column(col =>
            {
                // Driving-school master data (if filled in) - the document's letterhead.
                if (!string.IsNullOrWhiteSpace(settings.SchoolName))
                {
                    col.Item().Text(settings.SchoolName).FontSize(12).SemiBold().FontColor(Colors.Grey.Darken3);
                    var address = string.Join(", ", new[]
                    {
                        settings.SchoolStreet,
                        string.Join(" ", new[] { settings.SchoolPostalCode, settings.SchoolCity }.Where(s => !string.IsNullOrWhiteSpace(s))),
                    }.Where(s => !string.IsNullOrWhiteSpace(s)));
                    if (!string.IsNullOrWhiteSpace(address))
                        col.Item().Text(address).FontSize(9).FontColor(Colors.Grey.Darken1);
                    if (!string.IsNullOrWhiteSpace(settings.SchoolPermitNumber))
                        col.Item().Text($"Erlaubnisnummer: {settings.SchoolPermitNumber}").FontSize(9).FontColor(Colors.Grey.Darken1);
                    col.Item().PaddingBottom(6);
                }

                col.Item().Text("Ausbildungsnachweis").FontSize(18).Bold();
                col.Item().Text($"{student.FirstName} {student.LastName}").FontSize(12).SemiBold();
                var dob = student.DateOfBirth is { } d ? d.ToString("dd.MM.yyyy") : "—";
                col.Item().Text($"Geburtsdatum: {dob}").FontColor(Colors.Grey.Darken1);
                col.Item().PaddingBottom(6).Text($"Erstellt am {generatedOn:dd.MM.yyyy}").FontColor(Colors.Grey.Darken1);
                col.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
            });

            page.Content().PaddingVertical(10).Column(col =>
            {
                col.Spacing(14);

                if (progress.Classes.Count == 0)
                {
                    col.Item().Text("Für diesen Schüler ist noch keine Führerscheinklasse eingetragen.")
                        .Italic().FontColor(Colors.Grey.Darken1);
                }

                foreach (var c in progress.Classes)
                {
                    col.Item().Column(classCol =>
                    {
                        classCol.Item().PaddingTop(4).Text($"Klasse {c.Code} – Stand: {PhaseLabel(c.Phase)} ({c.DonePercent} % erledigt)")
                            .FontSize(13).Bold().FontColor(Colors.Blue.Darken2);

                        foreach (var section in c.Sections)
                        {
                            classCol.Item().PaddingTop(4).Text(section.Section).SemiBold();
                            foreach (var item in section.Items)
                            {
                                classCol.Item().Row(row =>
                                {
                                    row.RelativeItem().Text(BulletLine(item));
                                    row.ConstantItem(110).AlignRight().Text(StatusText(item))
                                        .FontColor(item.IsDone ? Colors.Green.Darken1 : Colors.Orange.Darken2);
                                });
                            }
                        }
                    });
                }

                // Exams
                col.Item().PaddingTop(6).Text("Prüfungen").FontSize(13).Bold().FontColor(Colors.Blue.Darken2);
                if (exams.Exams.Count == 0)
                {
                    col.Item().Text("Noch keine Prüfung eingetragen.").Italic().FontColor(Colors.Grey.Darken1);
                }
                else
                {
                    foreach (var e in exams.Exams)
                    {
                        col.Item().Row(row =>
                        {
                            var art = ExamArt(e);
                            var suffix = e.IsPreliminary ? " (Vermerk)" : $" ({e.AttemptNumber}. Versuch)";
                            row.RelativeItem().Text($"{art}{suffix} – Klasse {e.ClassCode}, {e.DateOn:dd.MM.yyyy}");
                            row.ConstantItem(110).AlignRight().Text(ExamResultLabel(e.Result))
                                .FontColor(ExamColor(e.Result));
                        });
                    }
                }
            });

            page.Footer().AlignCenter().Text(text =>
            {
                text.Span("Maschinell erstellt – ");
                text.CurrentPageNumber();
                text.Span(" / ");
                text.TotalPages();
            });
        });
    }

    private static string BulletLine(ProgressItemDto item)
    {
        var line = $"•  {item.Title}";
        if (item.IsCountable) line += $"  ({item.CurrentCount}/{item.RequiredCount})";
        return line;
    }

    private static string StatusText(ProgressItemDto item)
    {
        if (!item.IsDone) return "offen";
        return item.CompletedOn is { } d ? $"erledigt {d:dd.MM.yyyy}" : "erledigt";
    }

    private static string PhaseLabel(string phase) => phase switch
    {
        "Theory" => "Theorie",
        "TheoryExam" => "Theorieprüfung",
        "Practice" => "Praxis",
        "PracticeExam" => "Praxisprüfung",
        "Completed" => "Abgeschlossen",
        _ => phase,
    };

    private static string ExamArt(ExamDto e)
    {
        var baseLabel = e.Kind == "Theory" ? "Theorie" : "Praxis";
        return e.IsPreliminary ? $"{baseLabel}-Vorprüfung" : $"{baseLabel}prüfung";
    }

    private static string ExamResultLabel(string result) => result switch
    {
        "Passed" => "bestanden",
        "Failed" => "nicht bestanden",
        _ => "geplant",
    };

    private static string ExamColor(string result) => result switch
    {
        "Passed" => Colors.Green.Darken1,
        "Failed" => Colors.Red.Darken1,
        _ => Colors.Orange.Darken2,
    };
}
