using Fahrschule.Domain.Entities;

namespace Fahrschule.Application.Students;

/// <summary>
/// Pure business rules for the training progress - no database, easy to unit
/// test. Keeps the "what counts as done" and "how do we compute a percentage"
/// logic in one place so the service and the tests agree (KONZEPT 3.3).
/// </summary>
public static class StudentProgressRules
{
    /// <summary>A point is countable (has a +/− counter) when it carries a
    /// required-count value. Three kinds (KONZEPT 3.3):
    ///  - null  → simple check-off point (a theory topic, Grundfahraufgaben),
    ///  - 0     → a VOLUNTARY counter (extra lessons "über die Pflicht hinaus"),
    ///  - &gt; 0 → a mandatory counter with a target (e.g. Überlandfahrt 5x).</summary>
    public static bool IsCountable(int? requiredCount) => requiredCount.HasValue;

    /// <summary>A voluntary point (target 0) is optional: shown and countable,
    /// but it does not count toward the class completion / percentage.</summary>
    public static bool IsVoluntary(int? requiredCount) => requiredCount == 0;

    /// <summary>Counts toward the Pflicht (completion %)? Everything except the
    /// voluntary extra-lesson counters.</summary>
    public static bool IsRequired(StudentProgressItem item) => !IsVoluntary(item.RequiredCount);

    /// <summary>
    /// Is this point done? A mandatory counter is done once the counted sessions
    /// reach its target (KONZEPT 3.3: "bei Erreichen des Solls automatisch
    /// erledigt"). Voluntary counters are never "done" (no target). Simple points
    /// use the explicit completed flag.
    /// </summary>
    public static bool IsDone(StudentProgressItem item)
        => IsCountable(item.RequiredCount)
            ? item.RequiredCount!.Value > 0 && item.Entries.Count >= item.RequiredCount.Value
            : item.IsCompleted;

    /// <summary>
    /// Does a snapshotted point count for the given licence class? An empty
    /// class list means it applies to ALL of the student's classes (a shared
    /// "Grundstoff" point - KONZEPT 3.2/3.3).
    /// </summary>
    public static bool AppliesToClass(IReadOnlyCollection<Guid> itemClassIds, Guid classId)
        => itemClassIds.Count == 0 || itemClassIds.Contains(classId);

    /// <summary>Completed share in percent (0..100); 0 when there are no points.</summary>
    public static int Percent(int done, int total)
        => total <= 0 ? 0 : (int)Math.Round(done * 100.0 / total);

    /// <summary>A section counts as theory when its name starts with "Theorie"
    /// (e.g. "Theorie-Grundstoff"); everything else is practice. Mirrors the
    /// frontend grouping and the lesson-type derivation.</summary>
    public static bool IsTheorySection(string section)
        => section.TrimStart().StartsWith("Theorie", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The date a completed THEORY topic stays valid until: the last time it was
    /// taught plus the configured number of years. Null when expiry is off
    /// (<paramref name="validityYears"/> &lt;= 0) or the topic was never taught.
    /// </summary>
    public static DateOnly? TheoryValidUntil(DateOnly? lastTaughtOn, int validityYears)
        => validityYears > 0 && lastTaughtOn is { } d ? d.AddYears(validityYears) : null;

    /// <summary>
    /// Has a completed theory topic's validity lapsed as of <paramref name="today"/>?
    /// Expired means the last time it was taught is more than the configured years
    /// ago (KONZEPT: "nach 2 Jahren muss ein Theoriethema wiederholt werden").
    /// </summary>
    public static bool IsTheoryExpired(DateOnly? lastTaughtOn, int validityYears, DateOnly today)
        => TheoryValidUntil(lastTaughtOn, validityYears) is { } until && until < today;
}
