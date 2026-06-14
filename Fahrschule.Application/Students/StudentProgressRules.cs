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
}
