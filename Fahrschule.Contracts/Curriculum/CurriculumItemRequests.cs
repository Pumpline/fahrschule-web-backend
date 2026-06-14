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
/// "Update item" request. The editor decides what a content change (title,
/// target count, classes) does:
///  - AsNewVersion = true  → keep the old version, add a NEW one (only future/new
///    students follow it; existing snapshots stay on the old version).
///  - AsNewVersion = false → correct the existing version in place (applies
///    retroactively to everyone, e.g. fixing a typo).
/// Organisational-only changes (active/sort order) never create a version.
/// </summary>
public class UpdateCurriculumItemRequest
{
    [Required(ErrorMessage = "Bitte eine Bezeichnung für den Punkt eintragen.")]
    public string Title { get; set; } = string.Empty;

    public int? RequiredCount { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public Guid[] ClassIds { get; set; } = [];

    /// <summary>True = a content change becomes a new version; false = correct in place.</summary>
    public bool AsNewVersion { get; set; }

    /// <summary>Version marker against mutual overwrites.</summary>
    public uint RowVersion { get; set; }
}
