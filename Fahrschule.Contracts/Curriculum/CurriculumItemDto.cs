namespace Fahrschule.Contracts.Curriculum;

/// <summary>A curriculum item (currently valid version) as the API exposes it.</summary>
public class CurriculumItemDto
{
    public Guid Id { get; set; }

    /// <summary>Fixed identifier across all versions.</summary>
    public Guid ItemKey { get; set; }

    /// <summary>Version number of this revision (1, 2, 3 ...).</summary>
    public int Version { get; set; }

    /// <summary>Since when this revision applies.</summary>
    public DateTime ValidFromUtc { get; set; }

    public string Section { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public int? RequiredCount { get; set; }
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }

    /// <summary>Assigned classes (empty = applies to all classes).</summary>
    public Guid[] ClassIds { get; set; } = [];
    public string[] ClassCodes { get; set; } = [];

    /// <summary>Version marker against mutual overwrites (see LicenseClassDto).</summary>
    public uint RowVersion { get; set; }
}
