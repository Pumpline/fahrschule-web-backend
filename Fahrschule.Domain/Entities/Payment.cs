namespace Fahrschule.Domain.Entities;

/// <summary>
/// One paid item of a student (KONZEPT 3.6): either the money paid for a
/// practical lesson (<see cref="LessonId"/> set) or a freely entered item such
/// as "Grundbetrag" or "Prüfungsgebühr".
///
/// Money is <c>decimal</c> - never a floating point number (project rule 7).
/// The amount is what the student actually PAID (gross); net and VAT are
/// derived from <see cref="VatRatePercent"/> (see PaymentRules), because that is
/// the number the office has in hand.
///
/// As long as the item is not on an issued receipt it can be corrected and
/// deleted (soft-delete, audited). Once <see cref="ReceiptId"/> is set it is
/// frozen: the receipt is a tax document and must not change afterwards (GoBD).
/// Corrections then run through cancelling the receipt.
/// </summary>
public class PaymentItem
{
    public Guid Id { get; set; }

    public Guid StudentId { get; set; }
    public Student? Student { get; set; }

    /// <summary>Set when this is the money paid for one practical lesson.</summary>
    public Guid? LessonId { get; set; }
    public Lesson? Lesson { get; set; }

    /// <summary>The day the money was received.</summary>
    public DateOnly DateOn { get; set; }

    /// <summary>What was paid for, e.g. "Praxisstunde 45 Min." or "Grundbetrag".</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Gross amount actually paid.</summary>
    public decimal GrossAmount { get; set; }

    /// <summary>VAT rate of THIS item in percent (19, 7 or 0) - per position,
    /// so a mixed receipt stays possible.</summary>
    public int VatRatePercent { get; set; }

    /// <summary>Set once the item is on an issued receipt - then it is frozen.</summary>
    public Guid? ReceiptId { get; set; }
    public Receipt? Receipt { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    // Soft-delete (project rule 7) - only possible while not on a receipt.
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public Guid? DeletedByUserId { get; set; }
}

/// <summary>
/// An issued receipt ("Quittung", KONZEPT 3.6): the document confirming money
/// received. It carries a gapless consecutive number and is UNCHANGEABLE after
/// issuing (GoBD) - there is no edit and no delete, only cancelling
/// ("Storno"), which writes a second receipt with its own number that reverses
/// the first one.
///
/// The items are copied into <see cref="Items"/> when the receipt is issued
/// (a frozen snapshot), so a later change to a lesson can never alter a
/// document that has already been handed out. Kept for 10 years (§ 147 AO).
/// </summary>
public class Receipt
{
    public Guid Id { get; set; }

    public Guid StudentId { get; set; }
    public Student? Student { get; set; }

    /// <summary>Shown number, e.g. "2026-0007" (year + running number).</summary>
    public string Number { get; set; } = string.Empty;

    /// <summary>Year and running number of the series - the pair is unique, which
    /// is what makes the numbering gapless and verifiable.</summary>
    public int Year { get; set; }
    public int Sequence { get; set; }

    public DateTime IssuedAtUtc { get; set; }
    public Guid IssuedByUserId { get; set; }

    /// <summary>Name of the person who issued it (kept as text for the document).</summary>
    public string IssuedByName { get; set; } = string.Empty;

    /// <summary>Snapshot of the totals at the time of issuing.</summary>
    public decimal TotalNet { get; set; }
    public decimal TotalVat { get; set; }
    public decimal TotalGross { get; set; }

    /// <summary>Set on a CANCELLATION receipt: the receipt it reverses.</summary>
    public Guid? CancelsReceiptId { get; set; }

    /// <summary>Set on a cancelled receipt: the cancellation receipt.</summary>
    public Guid? CancelledByReceiptId { get; set; }

    /// <summary>Why it was cancelled (on the cancellation receipt).</summary>
    public string? CancelReason { get; set; }

    public List<ReceiptItem> Items { get; set; } = [];
}

/// <summary>
/// One frozen line of an issued receipt. Deliberately a copy, not a reference:
/// the document must still show exactly what was handed out years later.
/// </summary>
public class ReceiptItem
{
    public Guid Id { get; set; }

    public Guid ReceiptId { get; set; }
    public Receipt? Receipt { get; set; }

    public DateOnly DateOn { get; set; }
    public string Description { get; set; } = string.Empty;

    public decimal Net { get; set; }
    public int VatRatePercent { get; set; }
    public decimal VatAmount { get; set; }
    public decimal Gross { get; set; }

    /// <summary>Keeps the printed order stable.</summary>
    public int SortOrder { get; set; }
}
