using Fahrschule.Application.Students;
using Fahrschule.Domain.Entities;

namespace Fahrschule.Tests.Students;

/// <summary>Tests for the pure training-progress rules.</summary>
public class StudentProgressRulesTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(5, true)]
    public void IsCountable_only_for_positive_target(int? required, bool expected)
        => Assert.Equal(expected, StudentProgressRules.IsCountable(required));

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
}
