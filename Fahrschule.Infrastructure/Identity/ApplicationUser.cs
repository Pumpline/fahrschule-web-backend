using Microsoft.AspNetCore.Identity;

namespace Fahrschule.Infrastructure.Identity;

/// <summary>
/// Our user account (driving instructor, office staff, admin).
///
/// Inherits from IdentityUser&lt;Guid&gt; - the ready-made user class of
/// ASP.NET Core Identity. It already provides e-mail, hashed password
/// (never plain text!), account lockout after failed attempts, etc.
/// We only add the fields our driving school needs on top.
///
/// Why Guid as the key: random IDs cannot be guessed/enumerated
/// (better than 1, 2, 3 ...) and stay unique when merging data.
/// </summary>
public class ApplicationUser : IdentityUser<Guid>
{
    /// <summary>Display name shown in the UI, e.g. "Helga Muster".</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// True = the account has a temporary password (issued by the admin).
    /// After signing in, the user is forced to set their own password
    /// before they may use the application (see KONZEPT 3.7a).
    /// </summary>
    public bool MustChangePassword { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
