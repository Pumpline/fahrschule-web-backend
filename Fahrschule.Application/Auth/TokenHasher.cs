using System.Security.Cryptography;

namespace Fahrschule.Application.Auth;

/// <summary>
/// Hilfsfunktionen für Refresh-Tokens: erzeugen und hashen.
///
/// Bewusst eine kleine, "reine" Klasse ohne Abhängigkeiten – solche Logik
/// lässt sich besonders einfach mit Unit-Tests absichern (siehe Fahrschule.Tests).
/// </summary>
public static class TokenHasher
{
    /// <summary>
    /// Erzeugt einen kryptografisch zufälligen Token-Wert (64 Zufallsbytes,
    /// Base64-URL-kodiert). "Kryptografisch zufällig" heißt: nicht vorhersagbar –
    /// normale Zufallszahlen (wie UnityEngine.Random) wären hier unsicher.
    /// </summary>
    public static string GenerateRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    /// <summary>
    /// SHA-256-Hash eines Token-Werts als Hex-Text. In der Datenbank liegt nur
    /// dieser Hash: Aus ihm lässt sich das Original nicht zurückrechnen, aber
    /// ein vorgezeigtes Token lässt sich prüfen (gleicher Hash = gleiches Token).
    /// </summary>
    public static string Hash(string token)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }
}
