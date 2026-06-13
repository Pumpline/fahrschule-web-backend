using Fahrschule.Domain.Entities;

namespace Fahrschule.Application.Students;

/// <summary>
/// Pure business rules for the training progress - no database, easy to unit
/// test. Keeps the "what counts as done" and "how do we compute a percentage"
/// logic in one place so the service and the tests agree (KONZEPT 3.3).
/// </summary>
public static class StudentProgressRules
{
    /// <summary>A point is countable when it has a target count (e.g. special
    /// drives 5x); otherwise it is a simple check-off point.</summary>
    public static bool IsCountable(int? requiredCount) => requiredCount is > 0;

    /// <summary>
    /// Is this point done? Countable points are done once the counted sessions
    /// reach the target (KONZEPT 3.3: "bei Erreichen des Solls automatisch
    /// erledigt"). Simple points use the explicit completed flag.
    /// </summary>
    public static bool IsDone(StudentProgressItem item)
        => IsCountable(item.RequiredCount)
            ? item.Entries.Count >= item.RequiredCount!.Value
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
