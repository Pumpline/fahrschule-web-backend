using Fahrschule.Application.Students;

namespace Fahrschule.Tests.Students;

/// <summary>Tests for the per-student document checklist rules.</summary>
public class DocumentChecklistRulesTests
{
    private static readonly DateOnly Today = new(2026, 6, 13);

    [Fact]
    public void IsExpiringSoon_false_without_date()
    {
        Assert.False(DocumentChecklistRules.IsExpiringSoon(null, Today, 21));
    }

    [Fact]
    public void IsExpiringSoon_true_within_window()
    {
        // Expires in 10 days, window 21 → soon.
        Assert.True(DocumentChecklistRules.IsExpiringSoon(Today.AddDays(10), Today, 21));
    }

    [Fact]
    public void IsExpiringSoon_false_far_in_future()
    {
        // Expires in 40 days, window 21 → not yet.
        Assert.False(DocumentChecklistRules.IsExpiringSoon(Today.AddDays(40), Today, 21));
    }

    [Fact]
    public void IsExpiringSoon_true_when_already_expired()
    {
        Assert.True(DocumentChecklistRules.IsExpiringSoon(Today.AddDays(-3), Today, 21));
    }

    [Fact]
    public void DaysUntilExpiry_counts_correctly()
    {
        Assert.Equal(5, DocumentChecklistRules.DaysUntilExpiry(Today.AddDays(5), Today));
        Assert.Equal(-2, DocumentChecklistRules.DaysUntilExpiry(Today.AddDays(-2), Today));
        Assert.Null(DocumentChecklistRules.DaysUntilExpiry(null, Today));
    }

    [Fact]
    public void CheckCanBePresent_blocks_present_without_required_expiry()
    {
        var error = DocumentChecklistRules.CheckCanBePresent(isPresent: true, expiryDateRequired: true, expiresOn: null);
        Assert.NotNull(error);
        Assert.Contains("Ablaufdatum", error);
    }

    [Fact]
    public void CheckCanBePresent_allows_present_with_expiry()
    {
        Assert.Null(DocumentChecklistRules.CheckCanBePresent(true, expiryDateRequired: true, expiresOn: new DateOnly(2027, 1, 1)));
    }

    [Fact]
    public void CheckCanBePresent_allows_when_not_required()
    {
        Assert.Null(DocumentChecklistRules.CheckCanBePresent(true, expiryDateRequired: false, expiresOn: null));
    }
}
