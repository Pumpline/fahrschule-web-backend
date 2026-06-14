namespace Fahrschule.Contracts.Users;

/// <summary>A user account as the admin panel sees it.</summary>
public class UserDto
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>The single role of this user (Admin / Fahrlehrer / Verwaltung).</summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>True = still has a temporary password (has not signed in / set one yet).</summary>
    public bool MustChangePassword { get; set; }

    /// <summary>True = currently locked out (too many failed sign-in attempts).</summary>
    public bool IsLockedOut { get; set; }

    /// <summary>When the user last signed in successfully (UTC; null = never).</summary>
    public DateTime? LastLoginAtUtc { get; set; }
}

/// <summary>
/// Returned after creating a user or resetting a password: contains the
/// generated temporary password ONCE, so the admin can pass it on. It is
/// never stored in plain text and never appears in the audit log.
/// </summary>
public class TemporaryPasswordResultDto
{
    public UserDto User { get; set; } = new();
    public string TemporaryPassword { get; set; } = string.Empty;
}
