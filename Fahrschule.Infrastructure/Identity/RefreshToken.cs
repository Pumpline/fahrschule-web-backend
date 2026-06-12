namespace Fahrschule.Infrastructure.Identity;

/// <summary>
/// A refresh token: the "key to get a new key".
///
/// Background (common web concept): The actual access token (JWT) is only
/// valid for a short time (e.g. 15 minutes) - if it gets stolen, the damage
/// is limited. So that nobody has to sign in again every 15 minutes, this
/// long-lived refresh token exists. The browser sends it (as an httpOnly
/// cookie) to /api/auth/refresh and receives a fresh access token.
///
/// Security:
/// - We only store the SHA-256 HASH of the token, never the plain value
///   (same principle as passwords: a database leak reveals nothing).
/// - On every use the token is "rotated": the old one becomes invalid and
///   a new one is issued. A stolen, already-used token is therefore worthless.
/// </summary>
public class RefreshToken
{
    public Guid Id { get; set; }

    /// <summary>Which user does this token belong to?</summary>
    public Guid UserId { get; set; }
    public ApplicationUser? User { get; set; }

    /// <summary>SHA-256 hash of the token value (hex). The plain value only
    /// exists in the user's cookie.</summary>
    public string TokenHash { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>Set once the token has been revoked (logout, rotation,
    /// password change).</summary>
    public DateTime? RevokedAtUtc { get; set; }

    /// <summary>On rotation: hash of the successor token (helps detect theft).</summary>
    public string? ReplacedByTokenHash { get; set; }

    /// <summary>Is the token still usable at <paramref name="nowUtc"/>?</summary>
    public bool IsActive(DateTime nowUtc) => RevokedAtUtc is null && nowUtc < ExpiresAtUtc;
}
