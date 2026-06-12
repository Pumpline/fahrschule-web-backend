using Fahrschule.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Fahrschule.Infrastructure.Persistence;

/// <summary>
/// Bringt die Datenbank beim Start in einen benutzbaren Zustand:
/// 1. Ausstehende Migrationen anwenden (Schema aktuell halten).
/// 2. Die drei Rollen anlegen, falls sie fehlen.
/// 3. Einmalig den ersten Admin anlegen ("Seed") – die Zugangsdaten kommen
///    aus der Konfiguration (appsettings/Umgebungsvariablen, NIE fest im Code).
///
/// Der Admin-Seed läuft nur, solange noch kein einziger Benutzer existiert –
/// danach deaktiviert er sich von selbst (siehe KONZEPT, "Erster Admin per Seed").
/// </summary>
public static class DatabaseInitializer
{
    public static async Task InitializeAsync(IServiceProvider services)
    {
        // Ein "Scope" ist ein kurzlebiger Dienste-Behälter. DbContext & Co. sind
        // pro Anfrage gedacht; beim Start gibt es keine Anfrage, also bauen wir
        // uns selbst einen Scope (web-typisches Dependency-Injection-Muster).
        using var scope = services.CreateScope();
        var provider = scope.ServiceProvider;

        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseInitializer");
        var db = provider.GetRequiredService<FahrschuleDbContext>();

        // 1. Migrationen anwenden – legt beim allerersten Start auch die Datenbank an.
        await db.Database.MigrateAsync();

        // 2. Rollen anlegen, falls sie fehlen (idempotent: mehrfacher Start schadet nicht).
        var roleManager = provider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        foreach (var role in Roles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole<Guid>(role));
                logger.LogInformation("Rolle {Role} angelegt.", role);
            }
        }

        // 3. Ersten Admin anlegen – nur, wenn es noch gar keine Benutzer gibt.
        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        if (await userManager.Users.AnyAsync())
        {
            return; // Seed hat seinen Zweck erfüllt und bleibt künftig stumm.
        }

        var config = provider.GetRequiredService<IConfiguration>();
        var email = config["Seed:Admin:Email"];
        var password = config["Seed:Admin:Password"];
        var displayName = config["Seed:Admin:DisplayName"] ?? "Inhaber";

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning(
                "Kein Admin-Seed konfiguriert (Seed:Admin:Email / Seed:Admin:Password). " +
                "Ohne ersten Admin ist keine Anmeldung möglich.");
            return;
        }

        var admin = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = displayName,
            CreatedAtUtc = DateTime.UtcNow,
            // Das Seed-Passwort steht in einer Konfigurationsdatei und gilt damit
            // als "temporär": beim ersten Anmelden muss ein eigenes gesetzt werden.
            MustChangePassword = true,
        };

        var result = await userManager.CreateAsync(admin, password);
        if (!result.Succeeded)
        {
            var errors = string.Join("; ", result.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"Admin-Seed fehlgeschlagen: {errors}");
        }

        await userManager.AddToRoleAsync(admin, Roles.Admin);
        logger.LogInformation("Erster Admin {Email} wurde angelegt (Passwort muss beim ersten Login geändert werden).", email);
    }
}
