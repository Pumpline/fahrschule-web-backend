using Fahrschule.Application.Calendar;

namespace Fahrschule.Tests.Calendar;

/// <summary>Tests for the pure calendar overlap rule (KONZEPT 3.5).</summary>
public class CalendarRulesTests
{
    private static TimeOnly T(string s) => TimeOnly.Parse(s);

    [Fact]
    public void Overlapping_ranges_conflict()
        => Assert.True(CalendarRules.Overlaps(T("09:00"), T("10:00"), T("09:30"), T("10:30")));

    [Fact]
    public void Adjacent_ranges_do_not_conflict()
        => Assert.False(CalendarRules.Overlaps(T("09:00"), T("10:00"), T("10:00"), T("11:00")));

    [Fact]
    public void Separate_ranges_do_not_conflict()
        => Assert.False(CalendarRules.Overlaps(T("09:00"), T("10:00"), T("11:00"), T("12:00")));

    [Fact]
    public void Without_an_end_time_there_is_no_conflict()
    {
        Assert.False(CalendarRules.Overlaps(T("09:00"), null, T("09:00"), T("10:00")));
        Assert.False(CalendarRules.Overlaps(T("09:00"), T("10:00"), T("09:30"), null));
    }
}
