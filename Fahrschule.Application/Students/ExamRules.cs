namespace Fahrschule.Application.Students;

/// <summary>
/// Pure rules for the repeat lock after a failed exam (KONZEPT 3.4) - no
/// database, easy to unit test. The lock shortens automatically once enough
/// practice lessons are logged after the failed exam, so hours are only ever
/// entered in one place (the training progress).
/// </summary>
public static class ExamRules
{
    /// <summary>Did enough qualifying lessons happen to shorten the lock?</summary>
    public static bool IsShortened(int lessonsSince, int lessonsNeeded)
        => lessonsSince >= lessonsNeeded;

    /// <summary>The earliest date the exam may be retaken: the failed date plus
    /// the normal weeks, or the shortened weeks when enough lessons were logged.</summary>
    public static DateOnly LockEnd(DateOnly failedOn, bool shortened, int normalWeeks, int shortenedWeeks)
        => failedOn.AddDays((shortened ? shortenedWeeks : normalWeeks) * 7);
}
