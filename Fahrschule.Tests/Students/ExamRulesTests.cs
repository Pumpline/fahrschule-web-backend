using Fahrschule.Application.Students;

namespace Fahrschule.Tests.Students;

/// <summary>Tests for the pure repeat-lock rules (KONZEPT 3.4).</summary>
public class ExamRulesTests
{
    [Theory]
    [InlineData(0, 2, false)]
    [InlineData(1, 2, false)]
    [InlineData(2, 2, true)]
    [InlineData(3, 2, true)]
    public void IsShortened_needs_enough_lessons(int since, int needed, bool expected)
        => Assert.Equal(expected, ExamRules.IsShortened(since, needed));

    [Fact]
    public void LockEnd_uses_normal_weeks_when_not_shortened()
    {
        var failed = new DateOnly(2026, 5, 1);
        Assert.Equal(new DateOnly(2026, 5, 15), ExamRules.LockEnd(failed, shortened: false, normalWeeks: 2, shortenedWeeks: 1));
    }

    [Fact]
    public void LockEnd_uses_shortened_weeks_when_shortened()
    {
        var failed = new DateOnly(2026, 5, 1);
        Assert.Equal(new DateOnly(2026, 5, 8), ExamRules.LockEnd(failed, shortened: true, normalWeeks: 2, shortenedWeeks: 1));
    }
}
