using System.Globalization;
using System.Text.Json;
using Fahrschule.Application.Audit;
using Fahrschule.Application.Common;
using Fahrschule.Application.Settings;
using Fahrschule.Contracts.Students;
using Fahrschule.Domain.Entities;
using Fahrschule.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Fahrschule.Application.LicenseClasses;

namespace Fahrschule.Application.Payments;

public interface IPaymentService
{
    Task<PaymentOverviewDto> GetForStudentAsync(Guid studentId, CancellationToken ct = default);
    Task<PaymentOverviewDto> AddItemAsync(Guid studentId, SavePaymentItemRequest request, Actor actor, CancellationToken ct = default);
    Task<PaymentOverviewDto> UpdateItemAsync(Guid studentId, Guid itemId, SavePaymentItemRequest request, Actor actor, CancellationToken ct = default);
    Task<PaymentOverviewDto> DeleteItemAsync(Guid studentId, Guid itemId, Actor actor, CancellationToken ct = default);
    Task<PaymentOverviewDto> IssueReceiptAsync(Guid studentId, Actor actor, CancellationToken ct = default);
    Task<PaymentOverviewDto> CancelReceiptAsync(Guid studentId, Guid receiptId, CancelReceiptRequest request, Actor actor, CancellationToken ct = default);

    /// <summary>Writes/updates/removes the paid amount that belongs to a lesson.
    /// Called by the lesson service, so money lives in ONE place.</summary>
    Task SetLessonPaymentAsync(Lesson lesson, string? classCode, decimal? grossAmount, int? vatRatePercent, Actor actor, CancellationToken ct = default);

    /// <summary>Refuses a change when the lesson's money is already on a receipt.</summary>
    Task EnsureLessonMoneyEditableAsync(Guid lessonId, CancellationToken ct = default);
}

/// <summary>
/// Money of one student (KONZEPT 3.6). Two layers on purpose:
///
/// 1. <b>Zahlungsposten</b> (<see cref="PaymentItem"/>) - the working data: what
///    was paid for a lesson, plus freely entered items. Correctable and
///    deletable as long as it is not on a receipt yet.
/// 2. <b>Quittung</b> (<see cref="Receipt"/>) - the document. Issuing takes all
///    open items, gives them a gapless number and FREEZES a copy of them.
///    Afterwards nothing may change (GoBD): no edit, no delete, only a
///    cancellation receipt that reverses it and releases the items again.
///
/// VAT is carried per item (19 / 7 / 0 %), so a mixed receipt works. The gross
/// amount is what the student handed over; net and VAT follow from it
/// (see <see cref="PaymentRules"/>).
/// </summary>
public class PaymentService(
    FahrschuleDbContext db,
    ISettingsService settingsService,
    IAuditWriter auditWriter,
    ILogger<PaymentService> logger) : IPaymentService
{
    public async Task<PaymentOverviewDto> GetForStudentAsync(Guid studentId, CancellationToken ct = default)
    {
        await EnsureStudentAsync(studentId, ct);
        var settings = await settingsService.GetAsync(ct);

        var open = await db.PaymentItems
            .Where(i => i.StudentId == studentId && i.ReceiptId == null)
            .OrderBy(i => i.DateOn).ThenBy(i => i.CreatedAtUtc)
            .ToListAsync(ct);

        var receipts = await db.Receipts
            .Include(r => r.Items)
            .Where(r => r.StudentId == studentId)
            .OrderByDescending(r => r.IssuedAtUtc)
            .ToListAsync(ct);

        var numbers = receipts.ToDictionary(r => r.Id, r => r.Number);

        return new PaymentOverviewDto
        {
            OpenItems = open.Select(ToDto).ToList(),
            OpenTotalGross = PaymentRules.Round(open.Sum(i => i.GrossAmount)),
            Receipts = receipts.Select(r => ToDto(r, numbers)).ToList(),
            // A cancelled receipt and its cancellation add up to zero.
            ReceiptedTotalGross = PaymentRules.Round(receipts
                .Where(r => r.CancelsReceiptId == null && r.CancelledByReceiptId == null)
                .Sum(r => r.TotalGross)),
            DefaultVatRatePercent = settings.ReceiptVatRatePercent,
        };
    }

    public async Task<PaymentOverviewDto> AddItemAsync(
        Guid studentId, SavePaymentItemRequest request, Actor actor, CancellationToken ct = default)
    {
        await EnsureStudentAsync(studentId, ct);
        Validate(request);

        db.PaymentItems.Add(new PaymentItem
        {
            Id = Guid.NewGuid(),
            StudentId = studentId,
            DateOn = request.DateOn,
            Description = request.Description.Trim(),
            GrossAmount = PaymentRules.Round(request.GrossAmount),
            VatRatePercent = request.VatRatePercent,
            CreatedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(actor.UserId, actor.UserName, "Zahlung eingetragen",
            "Zahlung", studentId.ToString(),
            newValuesJson: JsonSerializer.Serialize(new
            {
                Datum = request.DateOn.ToString("dd.MM.yyyy"),
                Bezeichnung = request.Description.Trim(),
                Betrag = Money(request.GrossAmount),
                Steuersatz = request.VatRatePercent + " %",
            }), cancellationToken: ct);

        return await GetForStudentAsync(studentId, ct);
    }

    public async Task<PaymentOverviewDto> UpdateItemAsync(
        Guid studentId, Guid itemId, SavePaymentItemRequest request, Actor actor, CancellationToken ct = default)
    {
        await EnsureStudentAsync(studentId, ct);
        Validate(request);

        var item = await LoadItemAsync(studentId, itemId, ct);
        await EnsureNotOnReceiptAsync(item, ct);
        EnsureNotFromLesson(item, "Bitte den Betrag im Tab Ausbildungsfortschritt bei der Stunde selbst ändern.");

        var before = new
        {
            Datum = item.DateOn.ToString("dd.MM.yyyy"),
            Bezeichnung = item.Description,
            Betrag = Money(item.GrossAmount),
            Steuersatz = item.VatRatePercent + " %",
        };

        item.DateOn = request.DateOn;
        item.Description = request.Description.Trim();
        item.GrossAmount = PaymentRules.Round(request.GrossAmount);
        item.VatRatePercent = request.VatRatePercent;
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(actor.UserId, actor.UserName, "Zahlung geändert",
            "Zahlung", studentId.ToString(),
            oldValuesJson: JsonSerializer.Serialize(before),
            newValuesJson: JsonSerializer.Serialize(new
            {
                Datum = item.DateOn.ToString("dd.MM.yyyy"),
                Bezeichnung = item.Description,
                Betrag = Money(item.GrossAmount),
                Steuersatz = item.VatRatePercent + " %",
            }), cancellationToken: ct);

        return await GetForStudentAsync(studentId, ct);
    }

    public async Task<PaymentOverviewDto> DeleteItemAsync(
        Guid studentId, Guid itemId, Actor actor, CancellationToken ct = default)
    {
        await EnsureStudentAsync(studentId, ct);
        var item = await LoadItemAsync(studentId, itemId, ct);
        await EnsureNotOnReceiptAsync(item, ct);
        EnsureNotFromLesson(item, "Bitte den Betrag bei der Stunde selbst auf leer setzen.");

        item.IsDeleted = true;
        item.DeletedAtUtc = DateTime.UtcNow;
        item.DeletedByUserId = actor.UserId;
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(actor.UserId, actor.UserName, "Zahlung gelöscht",
            "Zahlung", studentId.ToString(),
            oldValuesJson: JsonSerializer.Serialize(new
            {
                Datum = item.DateOn.ToString("dd.MM.yyyy"),
                Bezeichnung = item.Description,
                Betrag = Money(item.GrossAmount),
            }), cancellationToken: ct);

        return await GetForStudentAsync(studentId, ct);
    }

    public async Task<PaymentOverviewDto> IssueReceiptAsync(Guid studentId, Actor actor, CancellationToken ct = default)
    {
        await EnsureStudentAsync(studentId, ct);

        var open = await db.PaymentItems
            .Where(i => i.StudentId == studentId && i.ReceiptId == null)
            .OrderBy(i => i.DateOn).ThenBy(i => i.CreatedAtUtc)
            .ToListAsync(ct);

        if (open.Count == 0)
        {
            throw new AppValidationException(
                "Es sind keine offenen Beträge vorhanden. Bitte zuerst einen Betrag eintragen.");
        }

        var now = DateTime.UtcNow;
        var receipt = new Receipt
        {
            Id = Guid.NewGuid(),
            StudentId = studentId,
            IssuedAtUtc = now,
            IssuedByUserId = actor.UserId,
            IssuedByName = actor.UserName,
        };

        var sort = 0;
        foreach (var item in open)
        {
            var (net, vat) = PaymentRules.SplitGross(item.GrossAmount, item.VatRatePercent);
            receipt.Items.Add(new ReceiptItem
            {
                Id = Guid.NewGuid(),
                DateOn = item.DateOn,
                Description = item.Description,
                Net = net,
                VatRatePercent = item.VatRatePercent,
                VatAmount = vat,
                Gross = PaymentRules.Round(item.GrossAmount),
                SortOrder = sort++,
            });
        }

        SetTotals(receipt);

        // Link the open items BEFORE saving. One SaveChanges is one database
        // transaction, so either the receipt AND the links exist, or neither.
        // Saving in two steps could leave a receipt behind whose amounts still
        // count as "open" - for money that must not happen.
        foreach (var item in open) item.ReceiptId = receipt.Id;

        await AssignNumberAndSaveAsync(receipt, now, ct);

        await auditWriter.WriteAsync(actor.UserId, actor.UserName, "Quittung ausgestellt",
            "Quittung", studentId.ToString(),
            newValuesJson: JsonSerializer.Serialize(new
            {
                Nummer = receipt.Number,
                Posten = receipt.Items.Count,
                Betrag = Money(receipt.TotalGross),
            }), cancellationToken: ct);

        return await GetForStudentAsync(studentId, ct);
    }

    public async Task<PaymentOverviewDto> CancelReceiptAsync(
        Guid studentId, Guid receiptId, CancelReceiptRequest request, Actor actor, CancellationToken ct = default)
    {
        await EnsureStudentAsync(studentId, ct);
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new AppValidationException("Bitte einen Grund für den Storno eintragen.");
        }

        var original = await db.Receipts.Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.Id == receiptId && r.StudentId == studentId, ct)
            ?? throw new NotFoundException("Diese Quittung wurde nicht gefunden. Bitte die Seite neu laden.");

        if (original.CancelledByReceiptId is not null)
        {
            throw new AppValidationException("Diese Quittung ist bereits storniert.");
        }
        if (original.CancelsReceiptId is not null)
        {
            throw new AppValidationException("Eine Storno-Quittung kann nicht noch einmal storniert werden.");
        }

        var now = DateTime.UtcNow;
        var cancellation = new Receipt
        {
            Id = Guid.NewGuid(),
            StudentId = studentId,
            IssuedAtUtc = now,
            IssuedByUserId = actor.UserId,
            IssuedByName = actor.UserName,
            CancelsReceiptId = original.Id,
            CancelReason = request.Reason.Trim(),
        };

        // The cancellation is the original with reversed signs - that is what
        // makes the two add up to zero in the books.
        foreach (var line in original.Items.OrderBy(i => i.SortOrder))
        {
            cancellation.Items.Add(new ReceiptItem
            {
                Id = Guid.NewGuid(),
                DateOn = line.DateOn,
                Description = "Storno: " + line.Description,
                Net = -line.Net,
                VatRatePercent = line.VatRatePercent,
                VatAmount = -line.VatAmount,
                Gross = -line.Gross,
                SortOrder = line.SortOrder,
            });
        }

        SetTotals(cancellation);

        // Again everything in ONE save: the cancellation receipt, the back-link
        // on the original and the released items belong together.
        original.CancelledByReceiptId = cancellation.Id;

        // The items become open again, so they can be corrected and put on a new
        // receipt. The cancelled receipt keeps its own frozen copy.
        var items = await db.PaymentItems.Where(i => i.ReceiptId == original.Id).ToListAsync(ct);
        foreach (var item in items) item.ReceiptId = null;

        await AssignNumberAndSaveAsync(cancellation, now, ct);

        await auditWriter.WriteAsync(actor.UserId, actor.UserName, "Quittung storniert",
            "Quittung", studentId.ToString(),
            oldValuesJson: JsonSerializer.Serialize(new { Nummer = original.Number, Betrag = Money(original.TotalGross) }),
            newValuesJson: JsonSerializer.Serialize(new { Storno = cancellation.Number, Grund = cancellation.CancelReason }),
            cancellationToken: ct);

        return await GetForStudentAsync(studentId, ct);
    }

    public async Task SetLessonPaymentAsync(
        Lesson lesson, string? classCode, decimal? grossAmount, int? vatRatePercent,
        Actor actor, CancellationToken ct = default)
    {
        var existing = await db.PaymentItems.FirstOrDefaultAsync(i => i.LessonId == lesson.Id, ct);

        if (existing?.ReceiptId is not null)
        {
            // Editing the lesson itself stays possible - only the AMOUNT is
            // frozen once it is on a receipt. An unchanged amount is no change.
            var unchanged = grossAmount is not null
                && PaymentRules.Round(grossAmount.Value) == existing.GrossAmount
                && (vatRatePercent is null || vatRatePercent == existing.VatRatePercent);
            if (unchanged) return;

            throw new AppValidationException(
                "Der bezahlte Betrag dieser Stunde steht auf Quittung "
                + await NumberOfAsync(existing.ReceiptId, ct)
                + ". Bitte die Quittung zuerst stornieren.");
        }

        // No amount (or zero): the item disappears again.
        if (grossAmount is null || grossAmount <= 0)
        {
            if (existing is not null)
            {
                existing.IsDeleted = true;
                existing.DeletedAtUtc = DateTime.UtcNow;
                existing.DeletedByUserId = actor.UserId;
            }
            return;
        }

        var settings = await settingsService.GetAsync(ct);
        var rate = vatRatePercent ?? settings.ReceiptVatRatePercent;
        var description = PaymentRules.LessonDescription(lesson.DurationMinutes, classCode);

        if (existing is null)
        {
            db.PaymentItems.Add(new PaymentItem
            {
                Id = Guid.NewGuid(),
                StudentId = lesson.StudentId,
                LessonId = lesson.Id,
                DateOn = lesson.DateOn,
                Description = description,
                GrossAmount = PaymentRules.Round(grossAmount.Value),
                VatRatePercent = rate,
                CreatedAtUtc = DateTime.UtcNow,
            });
        }
        else
        {
            existing.DateOn = lesson.DateOn;
            existing.Description = description;
            existing.GrossAmount = PaymentRules.Round(grossAmount.Value);
            existing.VatRatePercent = rate;
            existing.IsDeleted = false;
            existing.DeletedAtUtc = null;
            existing.DeletedByUserId = null;
        }
    }

    public async Task EnsureLessonMoneyEditableAsync(Guid lessonId, CancellationToken ct = default)
    {
        var item = await db.PaymentItems.FirstOrDefaultAsync(i => i.LessonId == lessonId, ct);
        if (item?.ReceiptId is null) return;

        throw new AppValidationException(
            "Der bezahlte Betrag dieser Stunde steht auf Quittung "
            + await NumberOfAsync(item.ReceiptId, ct)
            + ". Bitte die Quittung zuerst stornieren.");
    }

    // --- helpers ---

    /// <summary>
    /// Hands out the next number of the year and saves. On a collision it tries
    /// the next number: two people issuing at the same moment must never get the
    /// same number - the unique index on (year, sequence) catches that.
    ///
    /// Important: only a REAL collision is retried. Any other database error
    /// (missing migration, foreign key, value too long) is passed on, because
    /// retrying it three times changes nothing and the friendly "please try
    /// again" would hide the actual cause from the log.
    /// </summary>
    private async Task AssignNumberAndSaveAsync(Receipt receipt, DateTime now, CancellationToken ct)
    {
        var year = now.Year;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var last = await db.Receipts.Where(r => r.Year == year)
                .MaxAsync(r => (int?)r.Sequence, ct) ?? 0;

            receipt.Year = year;
            receipt.Sequence = last + 1;
            receipt.Number = PaymentRules.FormatNumber(year, receipt.Sequence);

            db.Receipts.Add(receipt);
            try
            {
                await db.SaveChangesAsync(ct);
                return;
            }
            catch (DbUpdateException ex)
            {
                // Detach the WHOLE graph - the lines too. Otherwise they stay
                // tracked as "Added" and the next Add() would not link them to
                // the receipt again.
                foreach (var line in receipt.Items)
                {
                    db.Entry(line).State = EntityState.Detached;
                }
                db.Entry(receipt).State = EntityState.Detached;

                // Was the number really taken in the meantime? Only then is a
                // retry meaningful.
                var numberTaken = await db.Receipts
                    .AnyAsync(r => r.Year == receipt.Year && r.Sequence == receipt.Sequence, ct);
                if (!numberTaken)
                {
                    logger.LogError(ex, "Quittung konnte nicht gespeichert werden (Nummer {Number})", receipt.Number);
                    throw;
                }

                logger.LogWarning("Quittungsnummer {Number} war bereits vergeben - neuer Versuch {Attempt}",
                    receipt.Number, attempt);
            }
        }

        throw new AppValidationException(
            "Die Quittung konnte gerade nicht ausgestellt werden, weil parallel gearbeitet wurde. "
            + "Bitte noch einmal versuchen.");
    }

    private static void SetTotals(Receipt receipt)
    {
        receipt.TotalNet = PaymentRules.Round(receipt.Items.Sum(i => i.Net));
        receipt.TotalVat = PaymentRules.Round(receipt.Items.Sum(i => i.VatAmount));
        receipt.TotalGross = PaymentRules.Round(receipt.Items.Sum(i => i.Gross));
    }

    private static void Validate(SavePaymentItemRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Description))
        {
            throw new AppValidationException("Bitte eine Bezeichnung eintragen, zum Beispiel Grundbetrag.");
        }
        if (request.GrossAmount <= 0)
        {
            throw new AppValidationException("Bitte einen Betrag größer als 0 Euro eintragen.");
        }
        if (request.GrossAmount > 100000m)
        {
            throw new AppValidationException("Bitte einen Betrag unter 100.000 Euro eintragen.");
        }
        if (request.VatRatePercent < 0 || request.VatRatePercent > 25)
        {
            throw new AppValidationException("Der Umsatzsteuersatz muss zwischen 0 und 25 Prozent liegen.");
        }
    }

    private async Task EnsureNotOnReceiptAsync(PaymentItem item, CancellationToken ct)
    {
        if (item.ReceiptId is null) return;

        throw new AppValidationException(
            "Dieser Posten steht auf Quittung " + await NumberOfAsync(item.ReceiptId, ct)
            + ". Eine ausgestellte Quittung darf nicht verändert werden - bitte die Quittung "
            + "stornieren und neu ausstellen.");
    }

    private static void EnsureNotFromLesson(PaymentItem item, string hint)
    {
        if (item.LessonId is null) return;
        throw new AppValidationException("Dieser Betrag gehört zu einer Fahrstunde. " + hint);
    }

    private async Task<string?> NumberOfAsync(Guid? receiptId, CancellationToken ct)
        => receiptId is null
            ? null
            : await db.Receipts.Where(r => r.Id == receiptId).Select(r => r.Number).FirstOrDefaultAsync(ct);

    private async Task<PaymentItem> LoadItemAsync(Guid studentId, Guid itemId, CancellationToken ct)
        => await db.PaymentItems.FirstOrDefaultAsync(i => i.Id == itemId && i.StudentId == studentId, ct)
            ?? throw new NotFoundException("Dieser Betrag wurde nicht gefunden. Bitte die Seite neu laden.");

    private async Task EnsureStudentAsync(Guid studentId, CancellationToken ct)
    {
        var exists = await db.Students.AnyAsync(s => s.Id == studentId, ct);
        if (!exists)
        {
            throw new NotFoundException("Dieser Schüler wurde nicht gefunden. Bitte die Liste neu laden.");
        }
    }

    private static PaymentItemDto ToDto(PaymentItem item)
    {
        var (net, vat) = PaymentRules.SplitGross(item.GrossAmount, item.VatRatePercent);
        return new PaymentItemDto
        {
            Id = item.Id,
            DateOn = item.DateOn,
            Description = item.Description,
            GrossAmount = item.GrossAmount,
            VatRatePercent = item.VatRatePercent,
            Net = net,
            VatAmount = vat,
            LessonId = item.LessonId,
        };
    }

    private static ReceiptDto ToDto(Receipt r, Dictionary<Guid, string> numbers) => new()
    {
        Id = r.Id,
        Number = r.Number,
        IssuedAtUtc = r.IssuedAtUtc,
        IssuedByName = r.IssuedByName,
        TotalNet = r.TotalNet,
        TotalVat = r.TotalVat,
        TotalGross = r.TotalGross,
        IsCancellation = r.CancelsReceiptId is not null,
        CancelsNumber = r.CancelsReceiptId is not null ? numbers.GetValueOrDefault(r.CancelsReceiptId.Value) : null,
        CancelledByNumber = r.CancelledByReceiptId is not null ? numbers.GetValueOrDefault(r.CancelledByReceiptId.Value) : null,
        CancelReason = r.CancelReason,
        Items = r.Items.OrderBy(i => i.SortOrder).Select(i => new ReceiptItemDto
        {
            DateOn = i.DateOn,
            Description = i.Description,
            Net = i.Net,
            VatRatePercent = i.VatRatePercent,
            VatAmount = i.VatAmount,
            Gross = i.Gross,
        }).ToList(),
    };

    private static string Money(decimal value)
        => value.ToString("N2", CultureInfo.GetCultureInfo("de-DE")) + " EUR";
}
