using System.ComponentModel.DataAnnotations;

namespace Fahrschule.Contracts.Documents;

/// <summary>"Create new document catalogue entry" request.</summary>
public class CreateDocumentCatalogItemRequest
{
    [Required(ErrorMessage = "Bitte einen Namen für die Unterlage eintragen (z. B. Sehtest).")]
    public string Name { get; set; } = string.Empty;

    public string? Note { get; set; }
    public bool ExpiryDateRequired { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    /// <summary>Empty = required for all classes.</summary>
    public Guid[] ClassIds { get; set; } = [];
}

/// <summary>"Update document catalogue entry" request - with version marker
/// against mutual overwrites.</summary>
public class UpdateDocumentCatalogItemRequest
{
    [Required(ErrorMessage = "Bitte einen Namen für die Unterlage eintragen (z. B. Sehtest).")]
    public string Name { get; set; } = string.Empty;

    public string? Note { get; set; }
    public bool ExpiryDateRequired { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public Guid[] ClassIds { get; set; } = [];

    public uint Version { get; set; }
}
