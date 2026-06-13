using Fahrschule.Application.Documents;

namespace Fahrschule.Tests.Documents;

/// <summary>Tests for the business rules of the document catalogue.</summary>
public class DocumentCatalogRulesTests
{
    [Theory]
    [InlineData("  Sehtest ", "Sehtest")]
    [InlineData("Erste-Hilfe", "Erste-Hilfe")]
    [InlineData(null, "")]
    public void NormalizeName_trims(string? input, string expected)
    {
        Assert.Equal(expected, DocumentCatalogRules.NormalizeName(input));
    }

    [Fact]
    public void Empty_name_is_rejected()
    {
        var errors = DocumentCatalogRules.Validate("");

        Assert.Single(errors);
        Assert.Contains("Namen", errors[0]); // German user-facing message
    }

    [Fact]
    public void Too_long_name_is_rejected()
    {
        var errors = DocumentCatalogRules.Validate(new string('X', DocumentCatalogRules.MaxNameLength + 1));

        Assert.Single(errors);
        Assert.Contains("höchstens", errors[0]);
    }

    [Fact]
    public void Valid_name_has_no_errors()
    {
        Assert.Empty(DocumentCatalogRules.Validate("Sehtest"));
    }
}
