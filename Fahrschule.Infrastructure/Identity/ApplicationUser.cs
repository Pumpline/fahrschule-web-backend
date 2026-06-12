using Microsoft.AspNetCore.Identity;

namespace Fahrschule.Infrastructure.Identity;

/// <summary>
/// Unser Benutzerkonto (Fahrlehrer, Verwaltung, Admin).
///
/// Erbt von IdentityUser&lt;Guid&gt; – das ist die fertige Benutzerklasse von
/// ASP.NET Core Identity. Sie bringt E-Mail, gehashtes Passwort (nie Klartext!),
/// Konto-Sperre nach Fehlversuchen usw. bereits mit. Wir ergänzen nur die
/// Felder, die unsere Fahrschule zusätzlich braucht.
///
/// Warum Guid als Schlüssel: zufällige IDs lassen sich nicht erraten/durchzählen
/// (besser als 1, 2, 3 …) und bleiben auch beim Zusammenführen von Daten eindeutig.
/// </summary>
public class ApplicationUser : IdentityUser<Guid>
{
    /// <summary>Anzeigename in der Oberfläche, z. B. "Helga Muster".</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// True = das Konto hat ein temporäres Passwort (vom Admin vergeben).
    /// Der Benutzer wird nach dem Anmelden gezwungen, ein eigenes Passwort
    /// zu setzen, bevor er die Anwendung benutzen darf (siehe KONZEPT 3.7a).
    /// </summary>
    public bool MustChangePassword { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
