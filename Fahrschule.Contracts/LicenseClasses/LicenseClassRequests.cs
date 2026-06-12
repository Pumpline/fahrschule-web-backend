using System.ComponentModel.DataAnnotations;

namespace Fahrschule.Contracts.LicenseClasses;

/// <summary>Anfrage "neue Führerscheinklasse anlegen".</summary>
public class CreateLicenseClassRequest
{
    [Required(ErrorMessage = "Bitte ein Kürzel für die Klasse eintragen (z. B. B, A1, BE).")]
    public string Code { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;
    public int? MinimumAge { get; set; }
    public string? Requirements { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}

/// <summary>Anfrage "Führerscheinklasse ändern" – mit Versionsmarke gegen
/// gegenseitiges Überschreiben (siehe LicenseClassDto.Version).</summary>
public class UpdateLicenseClassRequest
{
    [Required(ErrorMessage = "Bitte ein Kürzel für die Klasse eintragen (z. B. B, A1, BE).")]
    public string Code { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;
    public int? MinimumAge { get; set; }
    public string? Requirements { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    public uint Version { get; set; }
}
