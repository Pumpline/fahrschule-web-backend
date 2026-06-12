namespace Fahrschule.Infrastructure.Identity;

/// <summary>
/// Die drei Rollen der Anwendung (siehe CLAUDE.md, Grundsatz 4).
///
/// Als Konstanten definiert, damit Tippfehler beim Prüfen von Rollen
/// schon beim Kompilieren auffallen – nicht erst zur Laufzeit.
/// </summary>
public static class Roles
{
    /// <summary>Inhaber: darf alles, inklusive Adminpanel und DSGVO-Funktionen.</summary>
    public const string Admin = "Admin";

    /// <summary>Fahrlehrer: Stundeneintrag, Fortschritt, Termine – erhält Termin-Push.</summary>
    public const string Fahrlehrer = "Fahrlehrer";

    /// <summary>Verwaltung: sieht/bedient alles wie der Fahrlehrer, aber ohne Push-Benachrichtigungen.</summary>
    public const string Verwaltung = "Verwaltung";

    public static readonly string[] All = [Admin, Fahrlehrer, Verwaltung];
}
