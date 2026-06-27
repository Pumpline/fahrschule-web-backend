using Fahrschule.Application.Students;
using Fahrschule.Domain.Entities;

namespace Fahrschule.Tests.Students;

/// <summary>Tests for the pure training-progress rules.</summary>
public class StudentProgressRulesTests
{
    [Theory]
    [InlineData(null, false)] // simple check-off point
    [InlineData(0, true)]     // voluntary counter (no target)
    [InlineData(1, true)]
    [InlineData(5, true)]
    public void IsCountable_when_a_required_count_is_present(int? required, bool expected)
        => Assert.Equal(expected, StudentProgressRules.IsCountable(required));

    [Fact]
    public void Voluntary_counter_is_never_done_and_not_required()
    {
        var item = new StudentProgressItem { RequiredCount = 0 };
        item.Entries.Add(new StudentProgressEntry());
        Assert.True(StudentProgressRules.IsCountable(item.RequiredCount));
        Assert.False(StudentProgressRules.IsDone(item)); // no target → never "done"
        Assert.False(StudentProgressRules.IsRequired(item)); // optional → off the percentage
    }

    [Fact]
    public void IsDone_simple_point_uses_the_flag()
    {
        var item = new StudentProgressItem { RequiredCount = null, IsCompleted = true };
        Assert.True(StudentProgressRules.IsDone(item));

        item.IsCompleted = false;
        Assert.False(StudentProgressRules.IsDone(item));
    }

    [Fact]
    public void IsDone_countable_point_reaches_target_by_entry_count()
    {
        var item = new StudentProgressItem { RequiredCount = 3 };
        Assert.False(StudentProgressRules.IsDone(item));

        item.Entries.Add(new StudentProgressEntry());
        item.Entries.Add(new StudentProgressEntry());
        Assert.False(StudentProgressRules.IsDone(item)); // 2 of 3

        item.Entries.Add(new StudentProgressEntry());
        Assert.True(StudentProgressRules.IsDone(item)); // 3 of 3
    }

    [Fact]
    public void AppliesToClass_empty_list_means_all_classes()
    {
        var anyClass = Guid.NewGuid();
        Assert.True(StudentProgressRules.AppliesToClass([], anyClass));
    }

    [Fact]
    public void AppliesToClass_checks_membership_when_restricted()
    {
        var b = Guid.NewGuid();
        var a1 = Guid.NewGuid();
        Assert.True(StudentProgressRules.AppliesToClass([b], b));
        Assert.False(StudentProgressRules.AppliesToClass([b], a1));
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(0, 4, 0)]
    [InlineData(1, 4, 25)]
    [InlineData(3, 4, 75)]
    [InlineData(4, 4, 100)]
    public void Percent_rounds_the_share(int done, int total, int expected)
        => Assert.Equal(expected, StudentProgressRules.Percent(done, total));

    [Theory]
    [InlineData("Theorie-Grundstoff", true)]
    [InlineData("Theorie-Zusatzstoff", true)]
    [InlineData("Praxis", false)]
    [InlineData("Sonstiges", false)]
    public void IsTheorySection_matches_the_theorie_prefix(string section, bool expected)
        => Assert.Equal(expected, StudentProgressRules.IsTheorySection(section));

    [Fact]
    public void Theory_topic_expires_after_the_validity_period()
    {
        var taught = new DateOnly(2024, 6, 1);
        // 2-year validity: valid until 2026-06-01.
        Assert.Equal(new DateOnly(2026, 6, 1), StudentProgressRules.TheoryValidUntil(taught, 2));

        // Still valid the day before, expired the day after.
        Assert.False(StudentProgressRules.IsTheoryExpired(taught, 2, new DateOnly(2026, 5, 31)));
        Assert.True(StudentProgressRules.IsTheoryExpired(taught, 2, new DateOnly(2026, 6, 2)));
    }

    [Fact]
    public void Theory_expiry_is_off_when_validity_is_zero_or_no_date()
    {
        Assert.Null(StudentProgressRules.TheoryValidUntil(new DateOnly(2020, 1, 1), 0));
        Assert.Null(StudentProgressRules.TheoryValidUntil(null, 2));
        Assert.False(StudentProgressRules.IsTheoryExpired(new DateOnly(2000, 1, 1), 0, new DateOnly(2026, 1, 1)));
        Assert.False(StudentProgressRules.IsTheoryExpired(null, 2, new DateOnly(2026, 1, 1)));
    }
}
