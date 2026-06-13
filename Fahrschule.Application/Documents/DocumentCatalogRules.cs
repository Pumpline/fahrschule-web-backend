namespace Fahrschule.Application.Documents;

/// <summary>
/// Pure business rules for the document catalogue - no database dependencies,
/// so they are easy to unit test.
/// </summary>
public static class DocumentCatalogRules
{
    public const int MaxNameLength = 200;

    public static string NormalizeName(string? name) => (name ?? string.Empty).Trim();

    /// <summary>Validates the input and returns understandable German error
    /// messages (empty list = all good).</summary>
    public static List<string> Validate(string normalizedName)
    {
        var errors = new List<string>();

        if (string.IsNullOrEmpty(normalizedName))
        {
            errors.Add("Bitte einen Namen für die Unterlage eintragen (z. B. Sehtest).");
        }
        else if (normalizedName.Length > MaxNameLength)
        {
            errors.Add($"Der Name darf höchstens {MaxNameLength} Zeichen lang sein.");
        }

        return errors;
    }
}
