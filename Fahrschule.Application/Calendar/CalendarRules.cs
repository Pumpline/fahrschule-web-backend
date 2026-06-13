namespace Fahrschule.Application.Calendar;

/// <summary>
/// Pure calendar rules - no database, easy to unit test. The double-booking
/// check (KONZEPT 3.5) only applies when both appointments have an end time;
/// a fixed-start entry (e.g. an exam without an end) never blocks a slot.
/// </summary>
public static class CalendarRules
{
    /// <summary>Do two time ranges on the same day overlap?</summary>
    public static bool Overlaps(TimeOnly startA, TimeOnly? endA, TimeOnly startB, TimeOnly? endB)
    {
        if (endA is null || endB is null) return false;
        return startA < endB.Value && startB < endA.Value;
    }
}
