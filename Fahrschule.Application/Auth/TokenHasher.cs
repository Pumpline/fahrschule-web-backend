using System.Security.Cryptography;

namespace Fahrschule.Application.Auth;

/// <summary>
/// Helper functions for refresh tokens: generate and hash.
///
/// Deliberately a small, "pure" class without dependencies - logic like this
/// is particularly easy to cover with unit tests (see Fahrschule.Tests).
/// </summary>
public static class TokenHasher
{
    /// <summary>
    /// Generates a cryptographically random token value (64 random bytes,
    /// Base64-URL encoded). "Cryptographically random" means: unpredictable -
    /// ordinary random numbers would be insecure here.
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
    /// SHA-256 hash of a token value as hex text. Only this hash is stored in
    /// the database: the original cannot be derived from it, but a presented
    /// token can be verified (same hash = same token).
    /// </summary>
    public static string Hash(string token)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }
}
