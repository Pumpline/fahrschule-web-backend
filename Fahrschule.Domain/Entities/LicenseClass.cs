using Fahrschule.Domain.Common;

namespace Fahrschule.Domain.Entities;

/// <summary>
/// Eine Führerscheinklasse (B, BE, A1, …) – der erste Baustein des
/// Konfigurations-Fundaments (Projektregel 3: alles fachlich Veränderliche
/// ist DATEN, kein Code). Der Inhaber pflegt Klassen im Adminpanel;
/// bei Gesetzesänderungen wird hier editiert, nicht programmiert.
/// </summary>
public class LicenseClass : ISoftDeletable
{
    public Guid Id { get; set; }

    /// <summary>Das amtliche Kürzel, z. B. "B", "A1", "BE". Eindeutig.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Verständliche Beschreibung, z. B. "Pkw bis 3,5 t".</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Mindestalter in Jahren (für die spätere Eingabe-Prüfung beim
    /// Anmelden eines Schülers). null = kein Mindestalter hinterlegt.</summary>
    public int? MinimumAge { get; set; }

    /// <summary>Voraussetzungen als Freitext, z. B. "Vorbesitz Klasse B"
    /// oder "BF17: begleitetes Fahren ab 17 möglich".</summary>
    public string? Requirements { get; set; }

    /// <summary>Inaktiv = steht für NEUE Anmeldungen nicht mehr zur Auswahl;
    /// bestehende Schüler mit dieser Klasse behalten ihre Daten.
    /// (Deaktivieren ist bewusst etwas anderes als Löschen.)</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Reihenfolge in Listen (kleinere Zahl = weiter oben).</summary>
    public int SortOrder { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    // Soft-Delete (Projektregel 7): Löschen markiert nur; endgültiges
    // Entfernen übernimmt später der Aufbewahrungs-Job.
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public Guid? DeletedByUserId { get; set; }
}
