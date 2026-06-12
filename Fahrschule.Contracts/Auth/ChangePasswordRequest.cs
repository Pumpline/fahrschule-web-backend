using System.ComponentModel.DataAnnotations;

namespace Fahrschule.Contracts.Auth;

/// <summary>Anfrage "Passwort ändern" – auch für das erzwungene Ändern
/// nach der ersten Anmeldung mit temporärem Passwort.</summary>
public class ChangePasswordRequest
{
    [Required(ErrorMessage = "Bitte das aktuelle Passwort eintragen.")]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Bitte ein neues Passwort eintragen.")]
    public string NewPassword { get; set; } = string.Empty;
}
