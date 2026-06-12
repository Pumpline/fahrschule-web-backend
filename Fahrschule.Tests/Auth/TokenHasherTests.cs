using Fahrschule.Application.Auth;

namespace Fahrschule.Tests.Auth;

/// <summary>
/// Unit-Tests für die Token-Hilfsfunktionen.
///
/// Web-typisches Konzept "Unit-Test": prüft eine kleine Einheit Fachlogik
/// isoliert und automatisch. Läuft mit "dotnet test" – so merkt man sofort,
/// wenn eine spätere Änderung etwas kaputt macht (Sicherheitsnetz).
/// </summary>
public class TokenHasherTests
{
    [Fact]
    public void GenerateRefreshToken_erzeugt_jedes_Mal_einen_anderen_Wert()
    {
        var first = TokenHasher.GenerateRefreshToken();
        var second = TokenHasher.GenerateRefreshToken();

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void GenerateRefreshToken_ist_lang_genug_und_cookie_tauglich()
    {
        var token = TokenHasher.GenerateRefreshToken();

        // 64 Zufallsbytes ergeben Base64-kodiert mindestens 86 Zeichen.
        Assert.True(token.Length >= 86, $"Token zu kurz: {token.Length} Zeichen");

        // Nur URL-/Cookie-sichere Zeichen (kein '+', '/' oder '=').
        Assert.Matches("^[A-Za-z0-9_-]+$", token);
    }

    [Fact]
    public void Hash_ist_deterministisch_und_verraet_das_Original_nicht()
    {
        const string token = "beispiel-token";

        var hash1 = TokenHasher.Hash(token);
        var hash2 = TokenHasher.Hash(token);

        // Gleicher Eingabewert → gleicher Hash (sonst fände der Refresh das Token nie wieder).
        Assert.Equal(hash1, hash2);

        // SHA-256 = 32 Bytes = 64 Hex-Zeichen.
        Assert.Equal(64, hash1.Length);

        // Der Hash darf das Original nicht enthalten.
        Assert.DoesNotContain(token, hash1, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Hash_unterscheidet_verschiedene_Tokens()
    {
        Assert.NotEqual(TokenHasher.Hash("token-a"), TokenHasher.Hash("token-b"));
    }
}
