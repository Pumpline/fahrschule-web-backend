using System.Globalization;
using Fahrschule.Application.Common;
using Fahrschule.Application.Settings;
using Fahrschule.Application.Students;
using Fahrschule.Contracts.Settings;
using Fahrschule.Contracts.Students;
using Fahrschule.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Fahrschule.Application.Pdf;

public interface IReceiptPdfService
{
    Task<(byte[] Content, string FileName)> GenerateAsync(Guid studentId, Guid receiptId, CancellationToken ct = default);
}

/// <summary>
/// Prints one issued receipt ("Quittung", KONZEPT 3.6).
///
/// It prints the FROZEN copy stored with the receipt - never today's payment
/// items. That is the whole point of freezing: a document handed out in March
/// must still look the same in December, even if something was corrected in
/// between (GoBD).
/// </summary>
public class ReceiptPdfService(
    FahrschuleDbContext db,
    IStudentService students,
    ISettingsService settings) : IReceiptPdfService
{
    static ReceiptPdfService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public async Task<(byte[] Content, string FileName)> GenerateAsync(
        Guid studentId, Guid receiptId, CancellationToken ct = default)
    {
        var receipt = await db.Receipts
            .Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.Id == receiptId && r.StudentId == studentId, ct)
            ?? throw new NotFoundException("Diese Quittung wurde nicht gefunden. Bitte die Seite neu laden.");

        // Numbers of the linked receipt (cancellation <-> cancelled), for the note.
        var linkedId = receipt.CancelsReceiptId ?? receipt.CancelledByReceiptId;
        var linkedNumber = linkedId is null
            ? null
            : await db.Receipts.Where(r => r.Id == linkedId).Select(r => r.Number).FirstOrDefaultAsync(ct);

        var student = await students.GetByIdAsync(studentId, ct);
        var appSettings = await settings.GetAsync(ct);

        var model = new ReceiptPrintModel(
            receipt.Number,
            DateOnly.FromDateTime(receipt.IssuedAtUtc),
            receipt.IssuedByName,
            $"{student.FirstName} {student.LastName}".Trim(),
            [.. receipt.Items.OrderBy(i => i.SortOrder).Select(i => new ReceiptPrintLine(
                i.DateOn, i.Description, i.Net, i.VatRatePercent, i.VatAmount, i.Gross))],
            receipt.TotalNet,
            receipt.TotalVat,
            receipt.TotalGross,
            receipt.CancelsReceiptId is not null,
            receipt.CancelledByReceiptId is not null,
            linkedNumber,
            receipt.CancelReason);

        var bytes = new ReceiptDocument(model, appSettings).GeneratePdf();
        return (bytes, $"Quittung_{receipt.Number}.pdf");
    }
}

/// <summary>One printed line of the receipt.</summary>
public record ReceiptPrintLine(
    DateOnly DateOn, string Description, decimal Net, int VatRatePercent, decimal VatAmount, decimal Gross);

/// <summary>Everything the printed receipt needs - already frozen values.</summary>
public record ReceiptPrintModel(
    string Number,
    DateOnly IssuedOn,
    string IssuedByName,
    string StudentName,
    List<ReceiptPrintLine> Lines,
    decimal TotalNet,
    decimal TotalVat,
    decimal TotalGross,
    bool IsCancellation,
    bool IsCancelled,
    string? LinkedNumber,
    string? CancelReason);

/// <summary>The QuestPDF layout of a receipt (German - it is printed).</summary>
public class ReceiptDocument(ReceiptPrintModel model, AppSettingsDto settings) : IDocument
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(40);
            page.DefaultTextStyle(x => x.FontSize(10).FontColor(Colors.Black));

            page.Header().Column(Header);

            page.Content().PaddingVertical(10).Column(col =>
            {
                col.Spacing(14);

                Title(col);
                Parties(col);
                LineTable(col);
                Totals(col);
                VatSummary(col);
                ClosingWords(col);
            });

            page.Footer().AlignCenter().Text(text =>
            {
                text.DefaultTextStyle(x => x.FontSize(8).FontColor(Colors.Grey.Darken1));
                text.Span("Maschinell erstellt – Seite ");
                text.CurrentPageNumber();
                text.Span(" / ");
                text.TotalPages();
            });
        });
    }

    private void Header(ColumnDescriptor col)
    {
        if (Blank(settings.SchoolName)) return;

        col.Item().Text(settings.SchoolName).FontSize(12).SemiBold();
        var address = string.Join(", ", new[]
        {
            settings.SchoolStreet,
            string.Join(" ", new[] { settings.SchoolPostalCode, settings.SchoolCity }.Where(NotBlank)),
        }.Where(NotBlank));
        if (NotBlank(address)) col.Item().Text(address).FontSize(8).FontColor(Colors.Grey.Darken1);
        if (NotBlank(settings.SchoolTaxNumber))
        {
            col.Item().Text($"Steuernummer / USt-IdNr.: {settings.SchoolTaxNumber}")
                .FontSize(8).FontColor(Colors.Grey.Darken1);
        }
        col.Item().PaddingBottom(6);
    }

    private void Title(ColumnDescriptor col)
    {
        var heading = model.IsCancellation ? "Storno-Quittung" : "Quittung";
        col.Item().Row(row =>
        {
            row.RelativeItem().Text(heading).FontSize(16).Bold();
            row.ConstantItem(200).AlignRight().Column(right =>
            {
                right.Item().Text($"Nr. {model.Number}").FontSize(12).SemiBold();
                right.Item().Text($"vom {model.IssuedOn:dd.MM.yyyy}").FontSize(9);
            });
        });

        if (model.IsCancellation)
        {
            col.Item().Text($"Storniert die Quittung Nr. {model.LinkedNumber}.").FontSize(9).SemiBold();
            if (NotBlank(model.CancelReason))
            {
                col.Item().Text($"Grund: {model.CancelReason}").FontSize(9);
            }
        }
        else if (model.IsCancelled)
        {
            col.Item().Text($"Diese Quittung wurde storniert (Storno-Quittung Nr. {model.LinkedNumber}).")
                .FontSize(9).SemiBold();
        }
    }

    private void Parties(ColumnDescriptor col)
    {
        col.Item().Text(text =>
        {
            text.Span("Von ").FontSize(10);
            text.Span(model.StudentName).SemiBold();
            text.Span(model.IsCancellation
                ? " zurückerstattet:"
                : " dankend erhalten:");
        });
    }

    private void LineTable(ColumnDescriptor col)
    {
        col.Item().Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.ConstantColumn(62);   // Datum
                c.RelativeColumn();     // Bezeichnung
                c.ConstantColumn(62);   // Netto
                c.ConstantColumn(42);   // USt-Satz
                c.ConstantColumn(58);   // USt-Betrag
                c.ConstantColumn(66);   // Brutto
            });

            table.Header(header =>
            {
                HeaderCell(header, "Datum");
                HeaderCell(header, "Bezeichnung");
                HeaderCell(header, "Netto", right: true);
                HeaderCell(header, "USt", right: true);
                HeaderCell(header, "USt-Betrag", right: true);
                HeaderCell(header, "Brutto", right: true);
            });

            foreach (var line in model.Lines)
            {
                Cell(table, line.DateOn.ToString("dd.MM.yyyy"));
                Cell(table, line.Description);
                Cell(table, Money(line.Net), right: true);
                Cell(table, $"{line.VatRatePercent} %", right: true);
                Cell(table, Money(line.VatAmount), right: true);
                Cell(table, Money(line.Gross), right: true);
            }
        });
    }

    private void Totals(ColumnDescriptor col)
    {
        col.Item().AlignRight().Column(right =>
        {
            right.Item().Text($"Summe netto: {Money(model.TotalNet)} EUR").FontSize(10);
            right.Item().Text($"Umsatzsteuer: {Money(model.TotalVat)} EUR").FontSize(10);
            right.Item().PaddingTop(2).Text($"Gesamtbetrag: {Money(model.TotalGross)} EUR")
                .FontSize(13).Bold();
        });
    }

    /// <summary>Per rate, as required when several rates appear on one document.</summary>
    private void VatSummary(ColumnDescriptor col)
    {
        var groups = model.Lines
            .GroupBy(l => l.VatRatePercent)
            .OrderByDescending(g => g.Key)
            .ToList();
        if (groups.Count <= 1) return;

        col.Item().Column(inner =>
        {
            inner.Item().Text("Aufteilung nach Steuersätzen").FontSize(9).SemiBold();
            foreach (var group in groups)
            {
                inner.Item().Text(
                    $"{group.Key} %: netto {Money(group.Sum(l => l.Net))} EUR, "
                    + $"USt {Money(group.Sum(l => l.VatAmount))} EUR, "
                    + $"brutto {Money(group.Sum(l => l.Gross))} EUR").FontSize(9);
            }
        });
    }

    private void ClosingWords(ColumnDescriptor col)
    {
        col.Item().PaddingTop(10).Text(
            model.IsCancellation
                ? "Diese Storno-Quittung hebt die oben genannte Quittung vollständig auf."
                : "Der Betrag wurde erhalten.").FontSize(10);

        col.Item().PaddingTop(24).Row(row =>
        {
            row.RelativeItem().Column(left =>
            {
                left.Item().Text("__________________________________").FontSize(10);
                left.Item().Text("Ort, Datum").FontSize(8).FontColor(Colors.Grey.Darken1);
            });
            row.ConstantItem(24);
            row.RelativeItem().Column(rightCol =>
            {
                rightCol.Item().Text("__________________________________").FontSize(10);
                rightCol.Item().Text($"Unterschrift ({model.IssuedByName})")
                    .FontSize(8).FontColor(Colors.Grey.Darken1);
            });
        });
    }

    // --- small helpers ---

    private static void HeaderCell(TableCellDescriptor header, string text, bool right = false)
    {
        var cell = header.Cell().BorderBottom(1).BorderColor(Colors.Grey.Medium).PaddingVertical(3);
        (right ? cell.AlignRight() : cell.AlignLeft())
            .Text(text).FontSize(9).SemiBold();
    }

    private static void Cell(TableDescriptor table, string text, bool right = false)
    {
        var cell = table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingVertical(3);
        (right ? cell.AlignRight() : cell.AlignLeft()).Text(text).FontSize(9);
    }

    private static string Money(decimal value) => value.ToString("N2", De);

    private static bool NotBlank(string? value) => !string.IsNullOrWhiteSpace(value);
    private static bool Blank(string? value) => string.IsNullOrWhiteSpace(value);
}
