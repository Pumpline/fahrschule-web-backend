using Fahrschule.Application.Students;
using Fahrschule.Domain.Entities;

namespace Fahrschule.Tests.Students;

/// <summary>Tests for the pure student rules (age + progress).</summary>
public class StudentRulesTests
{
    [Fact]
    public void AgeInYears_handles_birthday_not_yet_reached()
    {
        var birth = new DateOnly(2008, 6, 15);
        // Day before the 18th birthday → still 17.
        Assert.Equal(17, StudentRules.AgeInYears(birth, new DateOnly(2026, 6, 14)));
        // On the birthday → 18.
        Assert.Equal(18, StudentRules.AgeInYears(birth, new DateOnly(2026, 6, 15)));
    }

    [Fact]
    public void CheckMinimumAge_allows_when_no_minimum_or_no_birthdate()
    {
        Assert.Null(StudentRules.CheckMinimumAge(new DateOnly(2010, 1, 1), null, new DateOnly(2026, 1, 1)));
        Assert.Null(StudentRules.CheckMinimumAge(null, 18, new DateOnly(2026, 1, 1)));
    }

    [Fact]
    public void CheckMinimumAge_allows_training_up_to_one_year_early()
    {
        // Class B (min 18): a 17-year-old may already start training.
        var birth = new DateOnly(2009, 1, 1); // turns 17 in 2026
        Assert.Null(StudentRules.CheckMinimumAge(birth, 18, new DateOnly(2026, 6, 1)));
    }

    [Fact]
    public void CheckMinimumAge_rejects_when_too_young()
    {
        // Class B (min 18): a 15-year-old is more than a year away → rejected.
        var birth = new DateOnly(2011, 1, 1); // turns 15 in 2026
        var result = StudentRules.CheckMinimumAge(birth, 18, new DateOnly(2026, 6, 1));
        Assert.NotNull(result);
        Assert.Contains("Mindestalter", result);
    }

    [Theory]
    [InlineData(StudentPhase.Theory, 10)]
    [InlineData(StudentPhase.Practice, 60)]
    [InlineData(StudentPhase.Completed, 100)]
    public void ProgressForPhase_maps_as_expected(StudentPhase phase, int expected)
    {
        Assert.Equal(expected, StudentRules.ProgressForPhase(phase));
    }

    [Fact]
    public void OverallProgress_averages_across_classes()
    {
        // Theory (10) + Completed (100) → average 55.
        var result = StudentRules.OverallProgress([StudentPhase.Theory, StudentPhase.Completed]);
        Assert.Equal(55, result);
    }

    [Fact]
    public void OverallProgress_is_zero_without_classes()
    {
        Assert.Equal(0, StudentRules.OverallProgress([]));
    }
}
