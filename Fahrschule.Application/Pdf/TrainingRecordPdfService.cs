using System.Globalization;
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
/// Generates the printable Ausbildungsnachweis (training record, KONZEPT 3.3/7):
/// a record of the student's recorded THEORY and PRACTICAL lessons (date, start
/// time, minutes, topic), modelled on the official form per § 31 Abs. 1 FahrlG /
/// § 6 Abs. 2 FahrSchAusbO. It is a content-equivalent of the official paper form
/// (which is copyrighted) - NOT a copy of its layout.
///
/// Built with QuestPDF (Community licence - allowed for this small family
/// business). Layout lives in <see cref="TrainingRecordDocument"/>. It reuses the
/// lesson service so the PDF shows exactly the entered lessons.
/// </summary>
public class TrainingRecordPdfService(
    IStudentService students,
    ILessonService lessons,
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
        var lessonList = await lessons.GetForStudentAsync(studentId, ct);
        var appSettings = await settings.GetAsync(ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var bytes = new TrainingRecordDocument(student, lessonList, appSettings, today).GeneratePdf();
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
    List<LessonDto> lessons,
    AppSettingsDto settings,
    DateOnly generatedOn) : IDocument
{
    private static readonly string Line = new(' ', 20); // a blank "fill-in" line

    public void Compose(IDocumentContainer container)
    {
        var theory = lessons.Where(l => l.Type == "Theory")
            .OrderBy(l => l.DateOn).ThenBy(l => l.StartTime).ToList();
        var practice = lessons.Where(l => l.Type == "Practice")
            .OrderBy(l => l.DateOn).ThenBy(l => l.StartTime).ToList();

        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(34);
            page.DefaultTextStyle(x => x.FontSize(9).FontColor(Colors.Black));

            page.Header().Column(col => Header(col));

            page.Content().PaddingVertical(8).Column(col =>
            {
                col.Spacing(12);

                StudentBlock(col);

                // --- Theoretical lessons (split: Grundunterricht vs class-specific) ---
                col.Item().Text("Theoretischer Unterricht").FontSize(11).Bold();
                var grund = theory.Where(l => l.LicenseClassId is null).ToList();
                var spez = theory.Where(l => l.LicenseClassId is not null).ToList();
                col.Item().Text("Grundunterricht (Grundstoff)").SemiBold().FontColor(Colors.Grey.Darken2);
                TheoryTable(col, grund);
                col.Item().PaddingTop(2).Text("Klassenspezifischer Unterricht (Zusatzstoff)").SemiBold().FontColor(Colors.Grey.Darken2);
                TheoryTable(col, spez);

                // --- Practical lessons ---
                col.Item().PaddingTop(4).Text("Praktische Ausbildung").FontSize(11).Bold();
                PracticeTable(col, practice);

                Summary(col, theory, practice);
                Legend(col);
                Signatures(col);
            });

            page.Footer().AlignCenter().Text(text =>
            {
                text.DefaultTextStyle(x => x.FontSize(8).FontColor(Colors.Grey.Darken1));
                text.Span($"Maschinell erstellt am {generatedOn:dd.MM.yyyy} – Seite ");
                text.CurrentPageNumber();
                text.Span(" / ");
                text.TotalPages();
            });
        });
    }

    private void Header(ColumnDescriptor col)
    {
        if (!string.IsNullOrWhiteSpace(settings.SchoolName))
        {
            col.Item().Text(settings.SchoolName).FontSize(11).SemiBold().FontColor(Colors.Grey.Darken3);
            var address = string.Join(", ", new[]
            {
                settings.SchoolStreet,
                string.Join(" ", new[] { settings.SchoolPostalCode, settings.SchoolCity }.Where(NotBlank)),
            }.Where(NotBlank));
            if (NotBlank(address)) col.Item().Text(address).FontSize(8).FontColor(Colors.Grey.Darken1);
            if (NotBlank(settings.SchoolPermitNumber))
                col.Item().Text($"Erlaubnisnummer: {settings.SchoolPermitNumber}").FontSize(8).FontColor(Colors.Grey.Darken1);
            col.Item().PaddingBottom(4);
        }

        col.Item().Text("Ausbildungsnachweis").FontSize(16).Bold();
        col.Item().Text("gemäß § 31 Abs. 1 Fahrlehrergesetz und § 6 Abs. 2 Fahrschüler-Ausbildungsordnung")
            .FontSize(8).Italic().FontColor(Colors.Grey.Darken1);
        col.Item().PaddingTop(4).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
    }

    private void StudentBlock(ColumnDescriptor col)
    {
        var dob = student.DateOfBirth is { } d ? d.ToString("dd.MM.yyyy") : Line;
        var classes = student.Classes.Count > 0 ? string.Join(", ", student.Classes.Select(c => c.Code)) : Line;

        col.Item().Row(row =>
        {
            row.RelativeItem().Column(left =>
            {
                Field(left, "Familienname", student.LastName);
                Field(left, "Vorname", student.FirstName);
                Field(left, "Anschrift", NotBlank(student.Address) ? student.Address! : Line);
                Field(left, "Geburtsdatum", dob);
            });
            row.ConstantItem(16);
            row.RelativeItem().Column(right =>
            {
                Field(right, "Schülerverzeichnis-Nr.", Line);
                Field(right, "Beantragte Klasse(n)", classes);
                Field(right, "Vorbesitz Klasse(n)", Line);
            });
        });
    }

    private static void Field(ColumnDescriptor col, string label, string value)
    {
        col.Item().PaddingBottom(2).Text(text =>
        {
            text.Span($"{label}: ").FontSize(8).FontColor(Colors.Grey.Darken1);
            text.Span(value).SemiBold();
        });
    }

    private void TheoryTable(ColumnDescriptor col, List<LessonDto> rows)
    {
        col.Item().Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.ConstantColumn(64);   // Datum
                c.RelativeColumn();     // Thema
                c.ConstantColumn(38);   // Min.
                c.ConstantColumn(30);   // FL
            });
            table.Header(h =>
            {
                Head(h.Cell(), "Datum");
                Head(h.Cell(), "Thema");
                Head(h.Cell(), "Min.", center: true);
                Head(h.Cell(), "FL", center: true);
            });

            if (rows.Count == 0)
            {
                table.Cell().ColumnSpan(4).Element(Cell).Text("—").FontColor(Colors.Grey.Darken1);
                return;
            }
            foreach (var l in rows)
            {
                Body(table.Cell(), l.DateOn.ToString("dd.MM.yyyy"));
                Body(table.Cell(), Topic(l));
                Body(table.Cell(), l.DurationMinutes.ToString(), center: true);
                Body(table.Cell(), InstructorNumber, center: true);
            }
        });
    }

    private void PracticeTable(ColumnDescriptor col, List<LessonDto> rows)
    {
        col.Item().Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.ConstantColumn(64);   // Datum
                c.RelativeColumn();     // Art u. Inhalt
                c.ConstantColumn(44);   // Beginn
                c.ConstantColumn(38);   // Min.
                c.ConstantColumn(30);   // FL
            });
            table.Header(h =>
            {
                Head(h.Cell(), "Datum");
                Head(h.Cell(), "Art u. Inhalt");
                Head(h.Cell(), "Beginn", center: true);
                Head(h.Cell(), "Min.", center: true);
                Head(h.Cell(), "FL", center: true);
            });

            if (rows.Count == 0)
            {
                table.Cell().ColumnSpan(5).Element(Cell).Text("—").FontColor(Colors.Grey.Darken1);
                return;
            }
            foreach (var l in rows)
            {
                Body(table.Cell(), l.DateOn.ToString("dd.MM.yyyy"));
                Body(table.Cell(), PracticeContent(l));
                Body(table.Cell(), l.StartTime, center: true);
                Body(table.Cell(), l.DurationMinutes.ToString(), center: true);
                Body(table.Cell(), InstructorNumber, center: true);
            }
        });
    }

    private void Summary(ColumnDescriptor col, List<LessonDto> theory, List<LessonDto> practice)
    {
        var tMin = theory.Sum(l => l.DurationMinutes);
        var pMin = practice.Sum(l => l.DurationMinutes);
        col.Item().PaddingTop(2).Text(
            $"Summe Theorie: {theory.Count} Einheiten / {tMin} Min.   ·   " +
            $"Summe Praxis: {practice.Count} Stunden / {pMin} Min.")
            .FontSize(8).SemiBold().FontColor(Colors.Grey.Darken2);
    }

    private void Legend(ColumnDescriptor col)
    {
        // Explain the FL number, naming the instructor behind it when known
        // ("FL = Fahrlehrer-Nummer (01 = Herr Rätze)").
        var text = NotBlank(settings.SchoolInstructorName)
            ? $"FL = Fahrlehrer-Nummer ({InstructorNumber} = {settings.SchoolInstructorName})"
            : "FL = Fahrlehrer-Nummer";
        col.Item().PaddingTop(4).Text(text).FontSize(7.5f).FontColor(Colors.Grey.Darken1);
    }

    private void Signatures(ColumnDescriptor col)
    {
        col.Item().PaddingTop(22).Row(row =>
        {
            SignatureLine(row.RelativeItem(), "Ort, Datum");
            row.ConstantItem(20);
            SignatureLine(row.RelativeItem(), "Unterschrift Fahrschulinhaber/-in");
            row.ConstantItem(20);
            SignatureLine(row.RelativeItem(), "Unterschrift Fahrschüler/-in");
        });
    }

    private static void SignatureLine(IContainer container, string label)
        => container.Column(c =>
        {
            c.Item().LineHorizontal(0.8f).LineColor(Colors.Grey.Darken1);
            c.Item().PaddingTop(2).Text(label).FontSize(8).FontColor(Colors.Grey.Darken1);
        });

    // --- cell styling + content helpers ---

    private static IContainer Cell(IContainer c)
        => c.Border(0.5f).BorderColor(Colors.Grey.Lighten1).PaddingVertical(3).PaddingHorizontal(5);

    // The alignment is applied to the TEXT inside the full-width, bordered cell
    // (not to the cell itself) - otherwise the border would shrink onto the text
    // and the number would look shifted out of its column.
    private static void Head(IContainer c, string text, bool center = false)
    {
        var cell = Cell(c).Background(Colors.Grey.Lighten3);
        if (center) cell = cell.AlignCenter();
        cell.Text(text).FontSize(8).SemiBold();
    }

    private static void Body(IContainer c, string text, bool center = false)
    {
        var cell = Cell(c);
        if (center) cell = cell.AlignCenter();
        cell.Text(text).FontSize(8.5f);
    }

    /// <summary>The instructor number printed in every FL row (single instructor;
    /// editable in the settings, default "01").</summary>
    private string InstructorNumber =>
        NotBlank(settings.SchoolInstructorNumber) ? settings.SchoolInstructorNumber : "01";

    private static string Topic(LessonDto l)
    {
        var topics = l.CoveredTitles.Count > 0 ? string.Join(", ", l.CoveredTitles) : null;
        return topics ?? l.Note ?? "—";
    }

    /// <summary>The full text entered for a practice lesson (covered topics or the
    /// free note). The "Art u. Inhalt" column is wide enough to hold it verbatim, so
    /// we print exactly what was entered instead of guessing a short code.</summary>
    private static string PracticeContent(LessonDto l)
    {
        var topic = l.CoveredTitles.Count > 0 ? string.Join(", ", l.CoveredTitles) : l.Note;
        return NotBlank(topic) ? topic! : "—";
    }

    private static bool NotBlank(string? value) => !string.IsNullOrWhiteSpace(value);
}
