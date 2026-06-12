namespace Fahrschule.Domain.Common;

/// <summary>
/// Kennzeichnet Entitäten, die "weich" gelöscht werden (Soft-Delete).
///
/// Warum: Löschen darf Daten nie sofort entfernen (Projektregel + DSGVO/AO §147).
/// Stattdessen wird nur markiert, dass der Datensatz gelöscht ist. Das echte
/// Entfernen übernimmt später ein Aufbewahrungs-Job nach Ablauf der gesetzlichen
/// Frist. Bis dahin kann ein Admin die Löschung rückgängig machen.
///
/// Unity-Brücke: vergleichbar mit "GameObject.SetActive(false)" statt "Destroy()" –
/// das Objekt ist unsichtbar, aber noch da.
/// </summary>
public interface ISoftDeletable
{
    /// <summary>Wurde der Datensatz als gelöscht markiert?</summary>
    bool IsDeleted { get; set; }

    /// <summary>Zeitpunkt der Lösch-Markierung (UTC).</summary>
    DateTime? DeletedAtUtc { get; set; }

    /// <summary>Wer hat gelöscht? (für das Audit-Log)</summary>
    Guid? DeletedByUserId { get; set; }
}
