namespace Fahrschule.Application.Curriculum;

/// <summary>
/// Pure business rules for curriculum items - no database dependencies,
/// so they are easy to cover with unit tests.
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
    /// Decides whether a change requires a NEW VERSION (KONZEPT 3.3a).
    ///
    /// New version for content changes (title, target count, class
    /// assignment) - these determine what a student has to learn; existing
    /// student checklists must be able to keep the old state.
    /// NO new version for purely organisational changes (active/sort order) -
    /// the content does not change there.
    /// </summary>
    public static bool NeedsNewVersion(
        string oldTitle, string newTitle,
        int? oldRequiredCount, int? newRequiredCount,
        IEnumerable<Guid> oldClassIds, IEnumerable<Guid> newClassIds)
    {
        if (!string.Equals(oldTitle, newTitle, StringComparison.Ordinal)) return true;
        if (oldRequiredCount != newRequiredCount) return true;

        // Compare the class assignment as a SET - order does not matter.
        var oldSet = new HashSet<Guid>(oldClassIds);
        return !oldSet.SetEquals(newClassIds);
    }
}
