namespace Fahrschule.Application.Payments;

/// <summary>
/// The pure money rules (KONZEPT 3.6) - no database, so they can be tested
/// directly. Everything is <c>decimal</c>: money must never run through a
/// floating point number (project rule 7), otherwise 0.1 + 0.2 no longer makes
/// exactly 0.30 and cents get lost.
/// </summary>
public static class PaymentRules
{
    /// <summary>
    /// Splits a GROSS amount into net and VAT for one item. The office knows
    /// what the student handed over, so the gross amount is the input and the
    /// net amount is derived - not the other way round.
    ///
    /// Rounded per item to full cents (commercial rounding); the receipt totals
    /// are the sums of the rounded items, so printed lines and totals match.
    /// </summary>
    public static (decimal Net, decimal Vat) SplitGross(decimal gross, int vatRatePercent)
    {
        if (vatRatePercent <= 0)
        {
            return (Round(gross), 0m);
        }

        var factor = 1m + vatRatePercent / 100m;
        var net = Round(gross / factor);
        return (net, Round(gross) - net);
    }

    /// <summary>Cents, rounding half away from zero (as on a paper receipt).</summary>
    public static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// The printed receipt number: year plus a running number, e.g. "2026-0007".
    /// A new series starts each year - inside a year the numbers are gapless,
    /// which is what the law asks for.
    /// </summary>
    public static string FormatNumber(int year, int sequence) => $"{year}-{sequence:D4}";

    /// <summary>Description proposed for the money paid for a lesson.</summary>
    public static string LessonDescription(int durationMinutes, string? classCode)
    {
        var suffix = string.IsNullOrWhiteSpace(classCode) ? "" : $" (Klasse {classCode})";
        return $"Fahrstunde {durationMinutes} Min.{suffix}";
    }

    /// <summary>
    /// Deadline for keeping a receipt: § 147 AO counts from the END of the year
    /// in which it was issued, so a receipt from March 2026 with 10 years runs
    /// until 31.12.2036.
    /// </summary>
    public static DateOnly RetentionEnd(DateOnly issuedOn, int years)
        => new DateOnly(issuedOn.Year, 12, 31).AddYears(years);
}
