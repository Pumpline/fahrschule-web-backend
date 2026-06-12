using Fahrschule.Application.LicenseClasses;

namespace Fahrschule.Tests.LicenseClasses;

/// <summary>Tests for the business rules of licence class maintenance.</summary>
public class LicenseClassRulesTests
{
    [Theory]
    [InlineData("  b ", "B")]
    [InlineData("b96", "B96")]
    [InlineData("A1", "A1")]
    [InlineData(null, "")]
    public void NormalizeCode_trims_and_upper_cases(string? input, string expected)
    {
        Assert.Equal(expected, LicenseClassRules.NormalizeCode(input));
    }

    [Fact]
    public void Empty_code_is_rejected()
    {
        var errors = LicenseClassRules.Validate("", minimumAge: null);

        Assert.Single(errors);
        Assert.Contains("Kürzel", errors[0]); // German user-facing message
    }

    [Fact]
    public void Too_long_code_is_rejected()
    {
        var errors = LicenseClassRules.Validate(new string('X', LicenseClassRules.MaxCodeLength + 1), null);

        Assert.Single(errors);
        Assert.Contains("höchstens", errors[0]);
    }

    [Theory]
    [InlineData(9)]    // below the plausibility bound (typo protection)
    [InlineData(100)]  // above it
    public void Implausible_minimum_age_is_rejected(int age)
    {
        var errors = LicenseClassRules.Validate("B", age);

        Assert.Single(errors);
        Assert.Contains("Mindestalter", errors[0]);
    }

    [Theory]
    [InlineData(null)] // no minimum age is allowed
    [InlineData(15)]
    [InlineData(18)]
    public void Valid_input_has_no_errors(int? age)
    {
        Assert.Empty(LicenseClassRules.Validate("B", age));
    }
}
