namespace Fahrschule.Contracts.Documents;

/// <summary>A document catalogue entry as the API exposes it.</summary>
public class DocumentCatalogItemDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Note { get; set; }
    public bool ExpiryDateRequired { get; set; }
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }

    /// <summary>Assigned classes (empty = required for all classes).</summary>
    public Guid[] ClassIds { get; set; } = [];
    public string[] ClassCodes { get; set; } = [];

    /// <summary>Version marker against mutual overwrites (see LicenseClassDto).</summary>
    public uint Version { get; set; }
}
