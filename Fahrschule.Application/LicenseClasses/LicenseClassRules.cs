namespace Fahrschule.Application.LicenseClasses;

/// <summary>
/// The pure business rules for licence classes - deliberately free of
/// database or web dependencies so they are easy to unit test
/// (see Fahrschule.Tests).
/// </summary>
public static class LicenseClassRules
{
    public const int MaxCodeLength = 10;

    /// <summary>Normalizes a code: trim whitespace, upper-case
    /// ("  b96 " → "B96"). This guarantees "b" and "B" are the same class.</summary>
    public static string NormalizeCode(string? code)
        => (code ?? string.Empty).Trim().ToUpperInvariant();

    /// <summary>
    /// Validates the input and returns understandable German error messages
    /// (empty list = all good). Deliberately only FORM checks - business
    /// values like the minimum age itself are editable data, not fixed rules
    /// in code (project rule 3).
    /// </summary>
    public static List<string> Validate(string normalizedCode, int? minimumAge)
    {
        var errors = new List<string>();

        if (string.IsNullOrEmpty(normalizedCode))
        {
            errors.Add("Bitte ein Kürzel für die Klasse eintragen (z. B. B, A1, BE).");
        }
        else if (normalizedCode.Length > MaxCodeLength)
        {
            errors.Add($"Das Kürzel darf höchstens {MaxCodeLength} Zeichen lang sein.");
        }

        // Plausibility bounds against typos (e.g. 180 instead of 18) -
        // not a business rule.
        if (minimumAge is < 10 or > 99)
        {
            errors.Add("Das Mindestalter muss zwischen 10 und 99 Jahren liegen (oder leer bleiben).");
        }

        return errors;
    }
}
