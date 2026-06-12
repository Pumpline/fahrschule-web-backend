using System.ComponentModel.DataAnnotations;

namespace Fahrschule.Contracts.Curriculum;

/// <summary>"Create new curriculum item" request.</summary>
public class CreateCurriculumItemRequest
{
    [Required(ErrorMessage = "Bitte den Abschnitt angeben (z. B. Theorie-Grundstoff).")]
    public string Section { get; set; } = string.Empty;

    [Required(ErrorMessage = "Bitte eine Bezeichnung für den Punkt eintragen.")]
    public string Title { get; set; } = string.Empty;

    public int? RequiredCount { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    /// <summary>Empty = applies to all classes.</summary>
    public Guid[] ClassIds { get; set; } = [];
}

/// <summary>
/// "Update item" request. Content changes (title, target count, classes)
/// automatically create a NEW VERSION - the old one is preserved.
/// </summary>
public class UpdateCurriculumItemRequest
{
    [Required(ErrorMessage = "Bitte eine Bezeichnung für den Punkt eintragen.")]
    public string Title { get; set; } = string.Empty;

    public int? RequiredCount { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public Guid[] ClassIds { get; set; } = [];

    /// <summary>Version marker against mutual overwrites.</summary>
    public uint RowVersion { get; set; }
}
