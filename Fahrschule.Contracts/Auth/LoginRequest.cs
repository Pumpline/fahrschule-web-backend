using System.ComponentModel.DataAnnotations;

namespace Fahrschule.Contracts.Auth;

/// <summary>
/// What the frontend sends when signing in.
///
/// Common web concept "DTO" (Data Transfer Object): a pure data package for
/// transport between frontend and backend - no logic. This keeps the internal
/// database structure separate from the API surface.
///
/// The [Required] attributes are validated by ASP.NET Core automatically
/// before the controller is even called ("model validation"). Error texts
/// are German - they are shown to users.
/// </summary>
public class LoginRequest
{
    [Required(ErrorMessage = "Bitte den Benutzernamen eintragen.")]
    public string UserName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Bitte das Passwort eintragen.")]
    public string Password { get; set; } = string.Empty;
}
