namespace Fahrschule.Domain.Entities;

/// <summary>
/// Ein Eintrag im Audit-Log: "Wer hat wann was geändert?"
///
/// Warum: Die DSGVO verlangt, dass Änderungen an personenbezogenen Daten
/// nachvollziehbar sind (Projektregel 1). Das Log ist "append-only" –
/// Einträge werden nur hinzugefügt, nie geändert oder gelöscht.
/// </summary>
public class AuditLog
{
    public long Id { get; set; }

    /// <summary>Zeitpunkt der Änderung (UTC – wir speichern Zeiten immer in UTC,
    /// die Anzeige rechnet in die deutsche Zeitzone um).</summary>
    public DateTime TimestampUtc { get; set; }

    /// <summary>Wer hat geändert? (null = System, z. B. automatischer Job)</summary>
    public Guid? UserId { get; set; }

    /// <summary>Anzeigename des Benutzers zum Zeitpunkt der Änderung.
    /// Bewusst als Kopie gespeichert, damit das Log auch dann lesbar bleibt,
    /// wenn der Benutzer später umbenannt oder gelöscht wird.</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>Was ist passiert? Z. B. "Angelegt", "Geändert", "Gelöscht",
    /// "Wiederhergestellt", "PasswortGeändert", "DatenExportiert".</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Welche Art von Datensatz? Z. B. "Schüler", "Benutzer".</summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>Schlüssel des betroffenen Datensatzes.</summary>
    public string EntityId { get; set; } = string.Empty;

    /// <summary>Werte vor der Änderung als JSON (null bei "Angelegt").
    /// Achtung Datensparsamkeit: niemals Passwörter oder Geheimnisse hier ablegen!</summary>
    public string? OldValuesJson { get; set; }

    /// <summary>Werte nach der Änderung als JSON (null bei "Gelöscht").</summary>
    public string? NewValuesJson { get; set; }
}
