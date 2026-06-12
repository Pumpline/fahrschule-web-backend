using System.ComponentModel.DataAnnotations;

namespace Fahrschule.Contracts.Auth;

/// <summary>
/// Was das Frontend beim Anmelden schickt.
///
/// Web-typisches Konzept "DTO" (Data Transfer Object): ein reines Datenpaket
/// für den Transport zwischen Frontend und Backend – ohne Logik. So bleibt die
/// interne Datenbankstruktur von der API getrennt (Unity-Brücke: wie ein
/// serialisierbares struct für Netzwerk-Nachrichten).
///
/// Die [Required]-Attribute prüft ASP.NET Core automatisch, bevor der
/// Controller überhaupt aufgerufen wird ("Model Validation").
/// </summary>
public class LoginRequest
{
    [Required(ErrorMessage = "Bitte die E-Mail-Adresse eintragen.")]
    [EmailAddress(ErrorMessage = "Das ist keine gültige E-Mail-Adresse.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Bitte das Passwort eintragen.")]
    public string Password { get; set; } = string.Empty;
}
