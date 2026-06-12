using Fahrschule.Application.Auth;

namespace Fahrschule.Tests.Auth;

/// <summary>
/// Unit tests for the token helper functions.
///
/// Common web concept "unit test": verifies a small unit of business logic
/// in isolation, automatically. Runs with "dotnet test" - so any later change
/// that breaks something is noticed immediately (safety net).
/// </summary>
public class TokenHasherTests
{
    [Fact]
    public void GenerateRefreshToken_produces_a_different_value_every_time()
    {
        var first = TokenHasher.GenerateRefreshToken();
        var second = TokenHasher.GenerateRefreshToken();

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void GenerateRefreshToken_is_long_enough_and_cookie_safe()
    {
        var token = TokenHasher.GenerateRefreshToken();

        // 64 random bytes encode to at least 86 Base64 characters.
        Assert.True(token.Length >= 86, $"Token too short: {token.Length} characters");

        // Only URL/cookie-safe characters (no '+', '/' or '=').
        Assert.Matches("^[A-Za-z0-9_-]+$", token);
    }

    [Fact]
    public void Hash_is_deterministic_and_does_not_reveal_the_original()
    {
        const string token = "example-token";

        var hash1 = TokenHasher.Hash(token);
        var hash2 = TokenHasher.Hash(token);

        // Same input → same hash (otherwise refresh could never find the token again).
        Assert.Equal(hash1, hash2);

        // SHA-256 = 32 bytes = 64 hex characters.
        Assert.Equal(64, hash1.Length);

        // The hash must not contain the original.
        Assert.DoesNotContain(token, hash1, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Hash_distinguishes_different_tokens()
    {
        Assert.NotEqual(TokenHasher.Hash("token-a"), TokenHasher.Hash("token-b"));
    }
}
