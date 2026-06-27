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

    // --- phase ("Stand") derivation (KONZEPT 3.3) -----------------------------
    // The Stand advances automatically from the bottom up - as sections complete
    // and exams are passed - but is only ever raised, never lowered (manual edits
    // may raise it further as an override). Setting the Stand forward also makes
    // the preceding sections count as 100 % complete (the owner's escape hatch
    // for the bureaucracy that is too detailed to model per class).

    private static StudentPhase Higher(StudentPhase a, StudentPhase b)
        => (StudentPhase)Math.Max((int)a, (int)b);

    /// <summary>
    /// The Stand justified by the actual progress, bottom up:
    ///  - all theory items done ⇒ ≥ Theorieprüfung,
    ///  - theory exam passed ⇒ ≥ Praxis,
    ///  - theory exam passed AND all practice items done ⇒ ≥ Praxisprüfung
    ///    (finished special drives only count once you are in the practice phase),
    ///  - practice exam passed ⇒ Fertig (a passed final exam means done, even if
    ///    earlier item data is incomplete).
    /// Always the HIGHEST milestone reached.
    /// </summary>
    public static StudentPhase DerivePhase(
        bool theoryItemsDone, bool theoryExamPassed, bool practiceItemsDone, bool practiceExamPassed)
    {
        var phase = StudentPhase.Theory;
        if (theoryItemsDone) phase = Higher(phase, StudentPhase.TheoryExam);
        if (theoryExamPassed) phase = Higher(phase, StudentPhase.Practice);
        if (theoryExamPassed && practiceItemsDone) phase = Higher(phase, StudentPhase.PracticeExam);
        if (practiceExamPassed) phase = Higher(phase, StudentPhase.Completed);
        return phase;
    }

    /// <summary>The Stand never moves backwards on its own: take the higher of the
    /// stored and the freshly derived phase.</summary>
    public static StudentPhase RaisePhase(StudentPhase stored, StudentPhase derived)
        => Higher(stored, derived);

    /// <summary>Does the Stand make the THEORY section count as complete?
    /// (Theorieprüfung or later - theory is done once you sit the theory exam.)</summary>
    public static bool TheoryCountsComplete(StudentPhase phase) => phase >= StudentPhase.TheoryExam;

    /// <summary>Does the Stand make the PRACTICE section count as complete?
    /// (Praxisprüfung or later.)</summary>
    public static bool PracticeCountsComplete(StudentPhase phase) => phase >= StudentPhase.PracticeExam;
}
