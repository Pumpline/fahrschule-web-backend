namespace Fahrschule.Contracts.Auth;

/// <summary>
/// Die Angaben zum angemeldeten Benutzer, die das Frontend kennen darf.
/// Bewusst sparsam (kein Passwort-Hash, keine internen Felder) – die API
/// zeigt nur, was die Oberfläche wirklich braucht.
/// </summary>
public class CurrentUserDto
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string[] Roles { get; set; } = [];

    /// <summary>True = temporäres Passwort; das Frontend leitet sofort zur
    /// Passwort-festlegen-Seite (Vollbild, erzwungen).</summary>
    public bool MustChangePassword { get; set; }
}
