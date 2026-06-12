namespace Fahrschule.Application.Curriculum;

/// <summary>
/// Reine Fachregeln für Ausbildungsplan-Punkte – ohne Datenbank, damit sie
/// per Unit-Test absicherbar sind.
/// </summary>
public static class CurriculumRules
{
    public const int MaxTitleLength = 300;

    public static string NormalizeTitle(string? title) => (title ?? string.Empty).Trim();

    public static List<string> Validate(string normalizedTitle, int? requiredCount)
    {
        var errors = new List<string>();

        if (string.IsNullOrEmpty(normalizedTitle))
        {
            errors.Add("Bitte eine Bezeichnung für den Punkt eintragen.");
        }
        else if (normalizedTitle.Length > MaxTitleLength)
        {
            errors.Add($"Die Bezeichnung darf höchstens {MaxTitleLength} Zeichen lang sein.");
        }

        if (requiredCount is < 1 or > 99)
        {
            errors.Add("Die Soll-Anzahl muss zwischen 1 und 99 liegen (oder leer bleiben für einen Abhak-Punkt).");
        }

        return errors;
    }

    /// <summary>
    /// Entscheidet, ob eine Änderung eine NEUE VERSION braucht (KONZEPT 3.3a).
    ///
    /// Neue Version bei inhaltlichen Änderungen (Bezeichnung, Soll-Anzahl,
    /// Klassen-Zuordnung) – denn davon hängt ab, was ein Schüler lernen muss;
    /// alte Schüler-Checklisten müssen den alten Stand behalten können.
    /// KEINE neue Version bei rein organisatorischen Änderungen
    /// (aktiv/inaktiv, Reihenfolge) – da ändert sich der Inhalt nicht.
    /// </summary>
    public static bool NeedsNewVersion(
        string oldTitle, string newTitle,
        int? oldRequiredCount, int? newRequiredCount,
        IEnumerable<Guid> oldClassIds, IEnumerable<Guid> newClassIds)
    {
        if (!string.Equals(oldTitle, newTitle, StringComparison.Ordinal)) return true;
        if (oldRequiredCount != newRequiredCount) return true;

        // Klassen-Zuordnung als MENGE vergleichen – die Reihenfolge ist egal.
        var alt = new HashSet<Guid>(oldClassIds);
        return !alt.SetEquals(newClassIds);
    }
}
