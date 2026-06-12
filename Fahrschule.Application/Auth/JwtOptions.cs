namespace Fahrschule.Application.Auth;

/// <summary>
/// Einstellungen für die Token-Erzeugung – kommen aus appsettings/Umgebungs-
/// variablen (Abschnitt "Jwt"). Das geheime Schlüsselmaterial steht NIEMALS
/// im Code oder im Repository (Projektregel: keine Secrets im Code).
/// </summary>
public class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>Wer stellt das Token aus? (unsere API)</summary>
    public string Issuer { get; set; } = "Fahrschule.Api";

    /// <summary>Für wen ist es bestimmt? (unser Frontend)</summary>
    public string Audience { get; set; } = "Fahrschule.Frontend";

    /// <summary>Geheimer Schlüssel zum Signieren (mindestens 32 Zeichen).
    /// Die Signatur stellt sicher, dass niemand Tokens fälschen kann.</summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>Lebensdauer des Zugriffstokens – kurz halten (Schadensbegrenzung bei Diebstahl).</summary>
    public int AccessTokenMinutes { get; set; } = 15;

    /// <summary>Lebensdauer des Refresh-Tokens – so lange bleibt man ohne neues Anmelden eingeloggt.</summary>
    public int RefreshTokenDays { get; set; } = 14;
}
