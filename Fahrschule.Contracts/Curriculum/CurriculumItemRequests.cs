using System.ComponentModel.DataAnnotations;

namespace Fahrschule.Contracts.Curriculum;

/// <summary>Anfrage "neuen Ausbildungsplan-Punkt anlegen".</summary>
public class CreateCurriculumItemRequest
{
    [Required(ErrorMessage = "Bitte den Abschnitt angeben (z. B. Theorie-Grundstoff).")]
    public string Section { get; set; } = string.Empty;

    [Required(ErrorMessage = "Bitte eine Bezeichnung für den Punkt eintragen.")]
    public string Title { get; set; } = string.Empty;

    public int? RequiredCount { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    /// <summary>Leer = gilt für alle Klassen.</summary>
    public Guid[] ClassIds { get; set; } = [];
}

/// <summary>
/// Anfrage "Punkt ändern". Inhaltliche Änderungen (Bezeichnung, Soll-Anzahl,
/// Klassen) erzeugen automatisch eine NEUE VERSION – die alte bleibt erhalten.
/// </summary>
public class UpdateCurriculumItemRequest
{
    [Required(ErrorMessage = "Bitte eine Bezeichnung für den Punkt eintragen.")]
    public string Title { get; set; } = string.Empty;

    public int? RequiredCount { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public Guid[] ClassIds { get; set; } = [];

    /// <summary>Versionsmarke gegen gegenseitiges Überschreiben.</summary>
    public uint RowVersion { get; set; }
}
