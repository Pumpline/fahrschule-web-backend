using Fahrschule.Application.Curriculum;

namespace Fahrschule.Tests.Curriculum;

/// <summary>Tests for the curriculum versioning rules (KONZEPT 3.3a).</summary>
public class CurriculumRulesTests
{
    private static readonly Guid ClassB = Guid.NewGuid();
    private static readonly Guid ClassA1 = Guid.NewGuid();

    [Fact]
    public void Changed_title_requires_a_new_version()
    {
        Assert.True(CurriculumRules.NeedsNewVersion(
            "Vorfahrt", "Vorfahrt und Verkehrsregelungen", null, null, [], []));
    }

    [Fact]
    public void Changed_required_count_requires_a_new_version()
    {
        // E.g. cross-country drives lowered from 5 to 4 → new legal state = new version.
        Assert.True(CurriculumRules.NeedsNewVersion("Überlandfahrt", "Überlandfahrt", 5, 4, [], []));
    }

    [Fact]
    public void Changed_class_assignment_requires_a_new_version()
    {
        Assert.True(CurriculumRules.NeedsNewVersion(
            "Thema", "Thema", null, null, [ClassB], [ClassB, ClassA1]));
    }

    [Fact]
    public void Same_classes_in_different_order_require_NO_new_version()
    {
        // Set comparison: [B, A1] and [A1, B] are the same assignment.
        Assert.False(CurriculumRules.NeedsNewVersion(
            "Thema", "Thema", null, null, [ClassB, ClassA1], [ClassA1, ClassB]));
    }

    [Fact]
    public void Active_or_sort_order_only_requires_NO_new_version()
    {
        // active/sort order are not even compared - identical content = no new version.
        Assert.False(CurriculumRules.NeedsNewVersion("Thema", "Thema", 3, 3, [ClassB], [ClassB]));
    }

    [Theory]
    [InlineData("", true)]      // empty → error
    [InlineData("Vorfahrt", false)]
    public void Title_is_validated(string title, bool errorExpected)
    {
        var errors = CurriculumRules.Validate(title, requiredCount: null);
        Assert.Equal(errorExpected, errors.Count > 0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void Implausible_required_count_is_rejected(int count)
    {
        var errors = CurriculumRules.Validate("Überlandfahrt", count);
        Assert.Single(errors);
        Assert.Contains("Soll-Anzahl", errors[0]); // German user-facing message
    }
}
