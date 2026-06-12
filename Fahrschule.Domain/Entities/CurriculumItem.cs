using Fahrschule.Domain.Common;

namespace Fahrschule.Domain.Entities;

/// <summary>
/// Ein Punkt des Ausbildungsplans – z. B. ein Theorie-Thema, später auch
/// Grundfahraufgaben und Sonderfahrten (KONZEPT 3.2/4).
///
/// Versionierung (KONZEPT 3.3a): Jeder Punkt hat eine FESTE Kennung
/// (<see cref="ItemKey"/>), die über alle Versionen gleich bleibt. Wird der
/// Inhalt geändert, entsteht eine NEUE Zeile mit Version+1; die alte Zeile
/// wird als "abgelöst" markiert (<see cref="SupersededAtUtc"/>) und bleibt
/// erhalten. So zeigt der Ausbildungsnachweis eines Schülers später genau
/// den Stand, der zu SEINER Zeit galt – Gesetzesänderungen wirken nie rückwirkend.
/// </summary>
public class CurriculumItem : ISoftDeletable
{
    public Guid Id { get; set; }

    /// <summary>Feste Kennung über alle Versionen hinweg (gleicher Punkt = gleicher Key).</summary>
    public Guid ItemKey { get; set; }

    /// <summary>Laufende Versionsnummer (1, 2, 3 …) je ItemKey.</summary>
    public int Version { get; set; }

    /// <summary>Ab wann gilt diese Version? (= Zeitpunkt der Änderung)</summary>
    public DateTime ValidFromUtc { get; set; }

    /// <summary>Gesetzt, sobald eine neuere Version existiert.
    /// null = das ist die aktuell gültige Version.</summary>
    public DateTime? SupersededAtUtc { get; set; }

    /// <summary>Abschnitt des Plans, z. B. "Theorie-Grundstoff", "Theorie-Zusatzstoff",
    /// später "Grundfahraufgaben", "Sonderfahrten". Bewusst Text statt fester
    /// Aufzählung – Abschnitte sind Daten, kein Code (Projektregel 3).</summary>
    public string Section { get; set; } = string.Empty;

    /// <summary>Bezeichnung des Punkts, z. B. "Vorfahrt und Verkehrsregelungen".</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Soll-Anzahl für zählbare Punkte (z. B. Überlandfahrt: 5).
    /// null = einfacher Abhak-Punkt (z. B. Theorie-Thema).</summary>
    public int? RequiredCount { get; set; }

    /// <summary>Abgeschaltete Punkte gelten für NEUE Anmeldungen nicht mehr
    /// ("Klasse X braucht kein Rückwärtseinparken mehr" → einfach abschalten).</summary>
    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }

    /// <summary>Für welche Klassen gilt der Punkt? LEER = gilt für ALLE Klassen
    /// (typisch für Grundstoff); sonst nur für die zugeordneten (KONZEPT 3.2:
    /// kein monolithischer "Grundstoff", Zuordnung je Punkt).</summary>
    public List<CurriculumItemClass> Classes { get; set; } = [];

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    // Soft-Delete (Projektregel 7)
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public Guid? DeletedByUserId { get; set; }
}

/// <summary>Verbindung Punkt ↔ Führerscheinklasse (M:N-Zwischentabelle).</summary>
public class CurriculumItemClass
{
    public Guid CurriculumItemId { get; set; }
    public CurriculumItem? CurriculumItem { get; set; }

    public Guid LicenseClassId { get; set; }
    public LicenseClass? LicenseClass { get; set; }
}
