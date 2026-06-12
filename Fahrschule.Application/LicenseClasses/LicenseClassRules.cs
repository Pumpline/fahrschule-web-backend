namespace Fahrschule.Application.LicenseClasses;

/// <summary>
/// Die reinen Fachregeln für Führerscheinklassen – bewusst ohne Datenbank-
/// oder Web-Abhängigkeiten, damit sie sich einfach per Unit-Test absichern
/// lassen (siehe Fahrschule.Tests).
/// </summary>
public static class LicenseClassRules
{
    public const int MaxCodeLength = 10;

    /// <summary>Bringt ein Kürzel in die Normalform: Leerraum weg, Großbuchstaben
    /// ("  b96 " → "B96"). So sind "b" und "B" garantiert dieselbe Klasse.</summary>
    public static string NormalizeCode(string? code)
        => (code ?? string.Empty).Trim().ToUpperInvariant();

    /// <summary>
    /// Prüft die Eingaben und liefert verständliche deutsche Fehlermeldungen
    /// (leere Liste = alles in Ordnung). Bewusst nur FORM-Prüfungen –
    /// fachliche Werte wie das Mindestalter selbst sind editierbare Daten,
    /// keine festen Regeln im Code (Projektregel 3).
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

        // Plausibilitätsgrenzen gegen Tippfehler (z. B. 180 statt 18) –
        // keine fachliche Festlegung.
        if (minimumAge is < 10 or > 99)
        {
            errors.Add("Das Mindestalter muss zwischen 10 und 99 Jahren liegen (oder leer bleiben).");
        }

        return errors;
    }
}
