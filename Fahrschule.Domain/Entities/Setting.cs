namespace Fahrschule.Domain.Entities;

/// <summary>
/// Ein einzelner Einstellungswert (Schlüssel → Wert).
///
/// Warum: Projektregel 3 – alles fachlich Veränderliche (Fristen, Vorlaufzeiten,
/// Sperr-Dauern …) wird als Daten gepflegt, nicht im Code festgeschrieben.
/// Das Adminpanel bearbeitet später genau diese Tabelle.
///
/// Unity-Brücke: vergleichbar mit einem ScriptableObject für Konfiguration –
/// nur dass die Werte in der Datenbank liegen und zur Laufzeit änderbar sind.
/// </summary>
public class Setting
{
    /// <summary>Eindeutiger Schlüssel, z. B. "Erinnerung.VorlaufMinuten".</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Der Wert als Text; die fachliche Logik wandelt ihn passend um.</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>Erklärung für das Adminpanel, was dieser Wert bewirkt.</summary>
    public string? Description { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
