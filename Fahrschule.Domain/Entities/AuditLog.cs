namespace Fahrschule.Domain.Entities;

/// <summary>
/// One audit log entry: "who changed what, and when?"
///
/// Why: GDPR requires that changes to personal data are traceable
/// (project rule 1). The log is append-only - entries are only ever
/// added, never modified or deleted.
///
/// Note: Action/EntityType values are stored in GERMAN on purpose -
/// the owner reads them directly in the admin panel.
/// </summary>
public class AuditLog
{
    public long Id { get; set; }

    /// <summary>When the change happened (UTC - we always store times in UTC;
    /// the UI converts to the German time zone for display).</summary>
    public DateTime TimestampUtc { get; set; }

    /// <summary>Who made the change? (null = system, e.g. an automated job)</summary>
    public Guid? UserId { get; set; }

    /// <summary>Display name of the user at the time of the change.
    /// Stored as a copy on purpose, so the log stays readable even if the
    /// user is renamed or deleted later.</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>What happened? E.g. "Angelegt", "Geändert", "Gelöscht",
    /// "Wiederhergestellt", "PasswortGeändert", "DatenExportiert".</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Which kind of record? E.g. "Schüler", "Benutzer".</summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>Topic this entry belongs to (a stable key like "students",
    /// "security"), derived from Action/EntityType at write time. Drives which
    /// role may see the entry in the change log (see AuditCategory).</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>Key of the affected record.</summary>
    public string EntityId { get; set; } = string.Empty;

    /// <summary>Values before the change as JSON (null for "created").
    /// Data minimisation: NEVER store passwords or secrets here!</summary>
    public string? OldValuesJson { get; set; }

    /// <summary>Values after the change as JSON (null for "deleted").</summary>
    public string? NewValuesJson { get; set; }
}
