using Fahrschule.Application.Payments;

namespace Fahrschule.Tests.Payments;

/// <summary>
/// The pure money rules (KONZEPT 3.6). These are the ones worth nailing down:
/// a wrong split or a wrong rounding shows up on a document that is handed out
/// and kept for ten years.
/// </summary>
public class PaymentRulesTests
{
    [Fact]
    public void Gross_is_split_into_net_and_vat()
    {
        // 119.00 EUR at 19 % = 100.00 net + 19.00 VAT
        var (net, vat) = PaymentRules.SplitGross(119m, 19);
        Assert.Equal(100.00m, net);
        Assert.Equal(19.00m, vat);
    }

    [Fact]
    public void Net_plus_vat_always_gives_the_gross_amount_back()
    {
        // Awkward amounts: rounding must never lose or invent a cent.
        decimal[] amounts = [0.01m, 4.99m, 45.00m, 49.95m, 99.99m, 123.45m, 1000m];
        foreach (var gross in amounts)
        {
            foreach (var rate in new[] { 0, 7, 19 })
            {
                var (net, vat) = PaymentRules.SplitGross(gross, rate);
                Assert.Equal(gross, net + vat);
            }
        }
    }

    [Fact]
    public void Without_vat_the_whole_amount_is_net()
    {
        var (net, vat) = PaymentRules.SplitGross(50m, 0);
        Assert.Equal(50m, net);
        Assert.Equal(0m, vat);
    }

    [Fact]
    public void Receipt_numbers_are_year_plus_a_running_number()
    {
        Assert.Equal("2026-0001", PaymentRules.FormatNumber(2026, 1));
        Assert.Equal("2026-0042", PaymentRules.FormatNumber(2026, 42));
        Assert.Equal("2027-1234", PaymentRules.FormatNumber(2027, 1234));
    }

    [Fact]
    public void Receipts_are_kept_until_the_end_of_the_tenth_following_year()
    {
        // § 147 AO counts from the end of the year the receipt was issued in.
        Assert.Equal(new DateOnly(2036, 12, 31),
            PaymentRules.RetentionEnd(new DateOnly(2026, 3, 5), 10));
    }
}
