using Fahrschule.Application.Settings;
using Fahrschule.Application.Students;
using Fahrschule.Contracts.Settings;
using Fahrschule.Contracts.Students;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Fahrschule.Application.Pdf;

public interface ITrainingContractPdfService
{
    Task<(byte[] Content, string FileName)> GenerateAsync(Guid studentId, CancellationToken ct = default);
}

/// <summary>
/// Generates the printable Ausbildungsvertrag (training contract, KONZEPT 1a/3a)
/// for a student: the parties (driving school + student), the requested licence
/// classes, the date and signature lines, plus the contract terms the owner
/// maintains as editable text (we never invent legal text). Built with QuestPDF
/// (Community licence). Layout lives in <see cref="TrainingContractDocument"/>.
/// </summary>
public class TrainingContractPdfService(
    IStudentService students,
    ISettingsService settings) : ITrainingContractPdfService
{
    static TrainingContractPdfService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public async Task<(byte[] Content, string FileName)> GenerateAsync(Guid studentId, CancellationToken ct = default)
    {
        var student = await students.GetByIdAsync(studentId, ct);
        var appSettings = await settings.GetAsync(ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var bytes = new TrainingContractDocument(student, appSettings, today).GeneratePdf();
        var fileName = $"Ausbildungsvertrag_{Sanitize(student.LastName)}_{Sanitize(student.FirstName)}.pdf";
        return (bytes, fileName);
    }

    private static string Sanitize(string value)
    {
        var cleaned = new string(value.Where(char.IsLetterOrDigit).ToArray());
        return string.IsNullOrEmpty(cleaned) ? "Schueler" : cleaned;
    }
}

/// <summary>The QuestPDF layout of the training contract (German - it is printed).</summary>
public class TrainingContractDocument(
    StudentDetailDto student,
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
                // Driving-school master data (if filled in) - the letterhead.
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

                col.Item().Text("Ausbildungsvertrag").FontSize(18).Bold();
                col.Item().PaddingBottom(6).Text($"Erstellt am {generatedOn:dd.MM.yyyy}").FontColor(Colors.Grey.Darken1);
                col.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
            });

            page.Content().PaddingVertical(10).Column(col =>
            {
                col.Spacing(12);

                // Parties.
                col.Item().Text("Vertragsparteien").FontSize(13).Bold().FontColor(Colors.Blue.Darken2);
                col.Item().Text(text =>
                {
                    text.Span("Fahrschule: ").SemiBold();
                    text.Span(string.IsNullOrWhiteSpace(settings.SchoolName) ? "—" : settings.SchoolName);
                });
                col.Item().Text(text =>
                {
                    text.Span("Fahrschüler/in: ").SemiBold();
                    text.Span($"{student.FirstName} {student.LastName}".Trim());
                });
                col.Item().Text($"Geburtsdatum: {(student.DateOfBirth is { } d ? d.ToString("dd.MM.yyyy") : "—")}")
                    .FontColor(Colors.Grey.Darken1);
                if (!string.IsNullOrWhiteSpace(student.Address))
                    col.Item().Text($"Anschrift: {student.Address}").FontColor(Colors.Grey.Darken1);
                var contact = string.Join(" · ", new[] { student.Phone, student.Email }.Where(s => !string.IsNullOrWhiteSpace(s)));
                if (!string.IsNullOrWhiteSpace(contact))
                    col.Item().Text($"Kontakt: {contact}").FontColor(Colors.Grey.Darken1);

                // Requested licence classes.
                col.Item().PaddingTop(4).Text("Beantragte Ausbildung").FontSize(13).Bold().FontColor(Colors.Blue.Darken2);
                if (student.Classes.Count == 0)
                {
                    col.Item().Text("Noch keine Führerscheinklasse eingetragen.").Italic().FontColor(Colors.Grey.Darken1);
                }
                else
                {
                    foreach (var c in student.Classes)
                    {
                        var desc = string.IsNullOrWhiteSpace(c.Description) ? "" : $" – {c.Description}";
                        col.Item().Text($"•  Klasse {c.Code}{desc}");
                    }
                }

                // Contract terms (the owner's editable text).
                col.Item().PaddingTop(4).Text("Vertragsbedingungen").FontSize(13).Bold().FontColor(Colors.Blue.Darken2);
                if (string.IsNullOrWhiteSpace(settings.ContractTerms))
                {
                    col.Item().Text("Es sind noch keine Vertragsbedingungen hinterlegt. Bitte im Adminpanel unter „Einstellungen“ eintragen.")
                        .Italic().FontColor(Colors.Grey.Darken1);
                }
                else
                {
                    col.Item().Text(settings.ContractTerms).LineHeight(1.3f);
                }

                // Signatures.
                col.Item().PaddingTop(20).Row(row =>
                {
                    row.RelativeItem().Column(sig =>
                    {
                        sig.Item().LineHorizontal(0.8f).LineColor(Colors.Grey.Darken1);
                        sig.Item().Text("Ort, Datum").FontSize(9).FontColor(Colors.Grey.Darken1);
                    });
                    row.ConstantItem(30);
                    row.RelativeItem().Column(sig =>
                    {
                        sig.Item().LineHorizontal(0.8f).LineColor(Colors.Grey.Darken1);
                        sig.Item().Text("Unterschrift Fahrschule").FontSize(9).FontColor(Colors.Grey.Darken1);
                    });
                });
                col.Item().PaddingTop(24).Row(row =>
                {
                    row.RelativeItem().Column(sig =>
                    {
                        sig.Item().LineHorizontal(0.8f).LineColor(Colors.Grey.Darken1);
                        sig.Item().Text("Unterschrift Fahrschüler/in (bei Minderjährigen: Erziehungsberechtigte/r)")
                            .FontSize(9).FontColor(Colors.Grey.Darken1);
                    });
                });
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
}
