using System.Security.Cryptography;

namespace Fahrschule.Application.Users;

/// <summary>
/// Generates a temporary password that the admin reads out to a new user.
///
/// Goals:
/// - satisfies our password policy (see Identity settings: at least 10 chars,
///   upper- and lowercase, a digit) so creation never fails,
/// - is easy to read aloud and type: a capitalised word + a short lowercase
///   block + digits, joined by hyphens; ambiguous characters (O/0, I/l/1)
///   are avoided.
///
/// Pure, dependency-free logic - covered by unit tests (Fahrschule.Tests).
/// </summary>
public static class TemporaryPasswordGenerator
{
    // Friendly, neutral words (capitalised → guarantees an uppercase letter).
    // Chosen to avoid the ambiguous lowercase 'l' so they read cleanly aloud.
    private static readonly string[] Words =
        ["Start", "Auto", "Fahrt", "Motor", "Reifen", "Strasse", "Tempo", "Verkehr"];

    // Lowercase letters without ambiguous ones (no l).
    private const string Lower = "abcdefghijkmnpqrstuvwxyz";
    // Digits without the ambiguous 0/1.
    private const string Digits = "23456789";

    public static string Generate()
    {
        var word = Words[RandomNumberGenerator.GetInt32(Words.Length)];
        var letters = RandomChars(Lower, 4); // guarantees lowercase
        var numbers = RandomChars(Digits, 3); // guarantees a digit

        // e.g. "Start-qmtx-639" → length ≥ 12, upper + lower + digit present.
        return $"{word}-{letters}-{numbers}";
    }

    private static string RandomChars(string alphabet, int count)
    {
        var chars = new char[count];
        for (var i = 0; i < count; i++)
        {
            chars[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        }
        return new string(chars);
    }
}
