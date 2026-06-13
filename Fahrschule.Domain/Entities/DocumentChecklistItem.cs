namespace Fahrschule.Domain.Entities;

/// <summary>
/// The status of ONE document for ONE student (KONZEPT 3.1). Which documents a
/// student needs is derived from the document catalogue and the student's
/// licence classes; this entity only stores the STATUS once it is filled in.
///
/// Data minimisation (GDPR, project rule 1): we never store the document/file
/// itself - only "present yes/no" plus the dates.
/// </summary>
public class DocumentChecklistItem
{
    public Guid StudentId { get; set; }
    public Student? Student { get; set; }

    public Guid DocumentCatalogItemId { get; set; }
    public DocumentCatalogItem? DocumentCatalogItem { get; set; }

    /// <summary>Has the document been presented? ("liegt vor")</summary>
    public bool IsPresent { get; set; }

    /// <summary>When it was presented (optional).</summary>
    public DateOnly? PresentedOn { get; set; }

    /// <summary>When it expires - drives the "bald fällig" reminder. May be
    /// mandatory depending on the catalogue item (ExpiryDateRequired).</summary>
    public DateOnly? ExpiresOn { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
