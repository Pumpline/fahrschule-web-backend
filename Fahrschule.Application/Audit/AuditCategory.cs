namespace Fahrschule.Application.Audit;

/// <summary>
/// Groups audit-log entries into a handful of human topics so the owner can
/// decide (in the admin panel) which role gets to see which topics - e.g. a
/// password change concerns only the Admin, not the instructors (KONZEPT 1/4,
/// data minimisation: each role sees only what it needs).
///
/// The category is DERIVED from the German Action/EntityType of an entry (see
/// <see cref="For"/>), so the many WriteAsync call sites don't have to pass it
/// explicitly - the writer computes it once. The keys are stable English
/// identifiers (stored in the database and used in role-visibility settings);
/// the labels are German because the owner reads them.
/// </summary>
public static class AuditCategory
{
    public const string Security = "security";
    public const string Users = "users";
    public const string Students = "students";
    public const string Training = "training";
    public const string Calendar = "calendar";
    public const string Setup = "setup";

    /// <summary>All categories with their German labels, in display order.</summary>
    public static readonly IReadOnlyList<(string Key, string Label)> All =
    [
        (Security, "Anmeldung & Sicherheit"),
        (Users, "Benutzer & Rechte"),
        (Students, "Schülerdaten"),
        (Training, "Ausbildung"),
        (Calendar, "Termine"),
        (Setup, "Einrichtung"),
    ];

    private static readonly Dictionary<string, string> Labels =
        All.ToDictionary(c => c.Key, c => c.Label);

    public static bool IsKnown(string key) => Labels.ContainsKey(key);

    public static string Label(string key) => Labels.GetValueOrDefault(key, key);

    /// <summary>
    /// Maps one audit entry to its category, based on the German Action and
    /// EntityType. Keep this in sync with the SQL backfill in the
    /// AuditKategorien migration. Anything unrecognised falls back to the most
    /// restrictive category (security = Admin only), so nothing leaks by default.
    /// </summary>
    public static string For(string action, string entityType) => entityType switch
    {
        // "Benutzer" covers both account management and password actions - split
        // by action so password events land in the Admin-only security topic.
        "Benutzer" => action is "PasswortGeändert" or "PasswortZurückgesetzt" ? Security : Users,
        "Schüler" => Students,
        "Ausbildungsfortschritt" or "Ausbildungsstunde" or "Prüfung" or "Unterlage-Schüler" => Training,
        "Termin" => Calendar,
        "Führerscheinklasse" or "Unterlage" or "Ausbildungsplan-Punkt" or "Einstellungen" => Setup,
        _ => Security,
    };
}
