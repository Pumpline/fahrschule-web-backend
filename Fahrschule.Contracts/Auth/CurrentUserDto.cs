namespace Fahrschule.Contracts.Auth;

/// <summary>
/// The signed-in user's details the frontend is allowed to know.
/// Deliberately minimal (no password hash, no internal fields) - the API
/// only exposes what the UI really needs.
/// </summary>
public class CurrentUserDto
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string[] Roles { get; set; } = [];

    /// <summary>True = temporary password; the frontend immediately redirects
    /// to the set-password page (full screen, enforced).</summary>
    public bool MustChangePassword { get; set; }
}
