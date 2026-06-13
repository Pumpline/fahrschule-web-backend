namespace Fahrschule.Contracts.Students;

/// <summary>One document in a student's checklist (catalogue item + status).</summary>
public class StudentDocumentDto
{
    public Guid CatalogItemId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Note { get; set; }

    /// <summary>If true, an expiry date is required before it can be ticked present.</summary>
    public bool ExpiryDateRequired { get; set; }

    public bool IsPresent { get; set; }
    public DateOnly? PresentedOn { get; set; }
    public DateOnly? ExpiresOn { get; set; }

    /// <summary>True = expires within the reminder window (or already expired).</summary>
    public bool ExpiresSoon { get; set; }

    /// <summary>Days until expiry (negative = expired); null if no expiry date.</summary>
    public int? DaysUntilExpiry { get; set; }
}

/// <summary>"Set the status of a document for a student" request.</summary>
public class SetStudentDocumentRequest
{
    public bool IsPresent { get; set; }
    public DateOnly? PresentedOn { get; set; }
    public DateOnly? ExpiresOn { get; set; }
}
