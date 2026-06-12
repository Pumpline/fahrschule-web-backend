using Fahrschule.Infrastructure.Identity;
using Fahrschule.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Fahrschule.Infrastructure;

/// <summary>
/// Registriert alle Infrastruktur-Dienste im Dependency-Injection-Container.
///
/// Web-typisches Konzept "Dependency Injection" (DI): Klassen erzeugen ihre
/// Abhängigkeiten nicht selbst (kein "new DbContext()"), sondern bekommen sie
/// vom Framework in den Konstruktor gereicht. Hier sagen wir dem Container,
/// WIE er die Dienste bauen soll. Vorteil: austauschbar und gut testbar.
///
/// Unity-Brücke: statt FindObjectOfType/Singleton-Managern gibt es einen
/// zentralen Container, der alle "Manager" kennt und verteilt.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "Verbindungszeichenfolge 'ConnectionStrings:Default' fehlt (appsettings oder Umgebungsvariable).");

        services.AddDbContext<FahrschuleDbContext>(options =>
            options.UseNpgsql(connectionString));

        // Identity = fertiges Benutzer-/Passwort-System von ASP.NET Core.
        // AddIdentityCore statt AddIdentity, weil wir KEINE Identity-Cookies/-Seiten
        // wollen – unsere Anmeldung läuft über die JSON-API mit eigenem JWT-Cookie.
        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                // Starke Passwörter erzwingen (Projektregel 7) – Länge zählt mehr
                // als Sonderzeichen-Pflicht (bedienerfreundlich für ältere Nutzer).
                options.Password.RequiredLength = 10;
                options.Password.RequireDigit = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireNonAlphanumeric = false;

                // Konto-Sperre gegen Passwort-Raten (Brute-Force-Schutz):
                // nach 5 Fehlversuchen 15 Minuten gesperrt.
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);

                options.User.RequireUniqueEmail = true;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<FahrschuleDbContext>();

        return services;
    }
}
