namespace Fahrschule.Contracts.Students;

/// <summary>
/// The money view of a student's file (KONZEPT 3.6): what is paid but not yet
/// receipted ("offen"), plus every receipt that has been issued.
/// </summary>
public class PaymentOverviewDto
{
    /// <summary>Paid items that are not on a receipt yet - still correctable.</summary>
    public List<PaymentItemDto> OpenItems { get; set; } = [];

    /// <summary>Sum of the open items (gross).</summary>
    public decimal OpenTotalGross { get; set; }

    /// <summary>Issued receipts, newest first (including cancelled ones).</summary>
    public List<ReceiptDto> Receipts { get; set; } = [];

    /// <summary>Sum of all receipts that are not cancelled and not a cancellation.</summary>
    public decimal ReceiptedTotalGross { get; set; }

    /// <summary>VAT rate proposed for a new item (from the settings).</summary>
    public int DefaultVatRatePercent { get; set; }
}

/// <summary>One paid item (from a lesson or entered freely).</summary>
public class PaymentItemDto
{
    public Guid Id { get; set; }
    public DateOnly DateOn { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal GrossAmount { get; set; }
    public int VatRatePercent { get; set; }
    public decimal Net { get; set; }
    public decimal VatAmount { get; set; }

    /// <summary>Set when the item belongs to a lesson - then the amount is
    /// changed at the lesson, not here.</summary>
    public Guid? LessonId { get; set; }
}

/// <summary>An issued receipt with its frozen lines.</summary>
public class ReceiptDto
{
    public Guid Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public DateTime IssuedAtUtc { get; set; }
    public string IssuedByName { get; set; } = string.Empty;

    public decimal TotalNet { get; set; }
    public decimal TotalVat { get; set; }
    public decimal TotalGross { get; set; }

    /// <summary>true = this receipt cancels another one ("Storno").</summary>
    public bool IsCancellation { get; set; }

    /// <summary>Number of the receipt this one cancels (on a cancellation).</summary>
    public string? CancelsNumber { get; set; }

    /// <summary>Number of the cancellation (on a cancelled receipt).</summary>
    public string? CancelledByNumber { get; set; }

    public string? CancelReason { get; set; }

    public List<ReceiptItemDto> Items { get; set; } = [];
}

/// <summary>One frozen line of a receipt.</summary>
public class ReceiptItemDto
{
    public DateOnly DateOn { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Net { get; set; }
    public int VatRatePercent { get; set; }
    public decimal VatAmount { get; set; }
    public decimal Gross { get; set; }
}

/// <summary>Entering or correcting a free item.</summary>
public class SavePaymentItemRequest
{
    public DateOnly DateOn { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal GrossAmount { get; set; }
    public int VatRatePercent { get; set; }
}

/// <summary>Cancelling an issued receipt ("Storno") - the reason is required.</summary>
public class CancelReceiptRequest
{
    public string Reason { get; set; } = string.Empty;
}
