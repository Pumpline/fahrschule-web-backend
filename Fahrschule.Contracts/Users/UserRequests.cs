using System.ComponentModel.DataAnnotations;

namespace Fahrschule.Contracts.Users;

/// <summary>"Create new user" request. The system generates a temporary
/// password; the admin passes it on. No password field here.</summary>
public class CreateUserRequest
{
    [Required(ErrorMessage = "Bitte eine E-Mail-Adresse eintragen.")]
    [EmailAddress(ErrorMessage = "Das ist keine gültige E-Mail-Adresse.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Bitte einen Namen eintragen.")]
    public string DisplayName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Bitte eine Rolle wählen.")]
    public string Role { get; set; } = string.Empty;
}

/// <summary>"Update user" request - change display name and role.</summary>
public class UpdateUserRequest
{
    [Required(ErrorMessage = "Bitte einen Namen eintragen.")]
    public string DisplayName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Bitte eine Rolle wählen.")]
    public string Role { get; set; } = string.Empty;
}
