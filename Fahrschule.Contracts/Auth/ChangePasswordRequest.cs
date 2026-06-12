using System.ComponentModel.DataAnnotations;

namespace Fahrschule.Contracts.Auth;

/// <summary>"Change password" request - also used for the forced change
/// after the first sign-in with a temporary password.</summary>
public class ChangePasswordRequest
{
    [Required(ErrorMessage = "Bitte das aktuelle Passwort eintragen.")]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Bitte ein neues Passwort eintragen.")]
    public string NewPassword { get; set; } = string.Empty;
}
