using Fahrschule.Domain.Common;

namespace Fahrschule.Domain.Entities;

/// <summary>
/// One entry of the document catalogue (KONZEPT 1b/3.1): which proof a class
/// requires - e.g. eyesight test, first-aid certificate, application form
/// from the authority. The catalogue drives the document checklist in the
/// student file: depending on the student's classes the matching documents
/// appear there.
///
/// Data minimisation (GDPR, project rule 1): we only ever track the STATUS
/// (present yes/no) plus the dates - never the documents/files themselves.
/// </summary>
public class DocumentCatalogItem : ISoftDeletable
{
    public Guid Id { get; set; }

    /// <summary>Name of the document, e.g. "Sehtest", "Erste-Hilfe-Bescheinigung".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional note for the office, e.g. "vom Amt ausgestellt".</summary>
    public string? Note { get; set; }

    /// <summary>
    /// If true, the document can only be ticked as "present" once an expiry
    /// date has been entered (e.g. the application form). Prevents forgetting
    /// the date (KONZEPT 3.1, "Ablaufdatum-Pflicht").
    /// </summary>
    public bool ExpiryDateRequired { get; set; }

    /// <summary>Disabled entries no longer apply to NEW registrations.</summary>
    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }

    /// <summary>Which classes require this document? EMPTY = required for ALL
    /// classes (the standard documents); otherwise only for the assigned ones
    /// (e.g. proof of aptitude only for C/D).</summary>
    public List<DocumentCatalogItemClass> Classes { get; set; } = [];

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    // Soft delete (project rule 7)
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public Guid? DeletedByUserId { get; set; }
}

/// <summary>Link between document catalogue entry and licence class (M:N join table).</summary>
public class DocumentCatalogItemClass
{
    public Guid DocumentCatalogItemId { get; set; }
    public DocumentCatalogItem? DocumentCatalogItem { get; set; }

    public Guid LicenseClassId { get; set; }
    public LicenseClass? LicenseClass { get; set; }
}
