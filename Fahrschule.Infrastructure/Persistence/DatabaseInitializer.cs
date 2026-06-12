using Fahrschule.Domain.Entities;
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

        // 3. Beispiel-Führerscheinklassen anlegen – nur, wenn die Tabelle leer ist.
        //    Das sind STARTWERTE (gängige Klassen + Mindestalter, Stand der
        //    Recherche 2026 – bitte fachlich prüfen!). Alles ist danach im
        //    Adminpanel änderbar; der Code legt hier nichts dauerhaft fest.
        if (!await db.LicenseClasses.AnyAsync())
        {
            var now = DateTime.UtcNow;
            LicenseClass Neu(string code, string beschreibung, int? alter, string? voraussetzungen, int reihenfolge) => new()
            {
                Id = Guid.NewGuid(), Code = code, Description = beschreibung,
                MinimumAge = alter, Requirements = voraussetzungen,
                IsActive = true, SortOrder = reihenfolge, CreatedAtUtc = now, UpdatedAtUtc = now,
            };

            db.LicenseClasses.AddRange(
                Neu("AM", "Moped/Roller bis 45 km/h", 15, "Mindestalter je nach Bundesland 15 oder 16 Jahre", 10),
                Neu("A1", "Leichtkrafträder bis 125 cm³", 16, null, 20),
                Neu("A2", "Krafträder bis 35 kW", 18, "Direkteinstieg oder Aufstieg von A1 nach 2 Jahren", 30),
                Neu("A", "Krafträder unbeschränkt", 24, "Direkteinstieg ab 24; Aufstieg von A2 nach 2 Jahren ab 20", 40),
                Neu("B", "Pkw bis 3,5 t", 17, "Mit 17 nur begleitetes Fahren (BF17); allein ab 18", 50),
                Neu("BE", "Pkw mit Anhänger über 750 kg", 17, "Vorbesitz Klasse B erforderlich", 60),
                Neu("B96", "Pkw mit Anhänger (Zug bis 4,25 t)", 17, "Erweiterung zu Klasse B (nur Schulung, keine Prüfung)", 70),
                Neu("L", "Land-/forstwirtschaftliche Zugmaschinen bis 40 km/h", 16, null, 80),
                Neu("T", "Land-/forstwirtschaftliche Zugmaschinen bis 60 km/h", 16, null, 90));

            await db.SaveChangesAsync();
            logger.LogInformation("Beispiel-Führerscheinklassen angelegt (Startwerte, im Adminpanel änderbar).");
        }

        // 4. Beispiel-Theoriethemen anlegen – nur, wenn die Tabelle leer ist.
        //    STARTWERTE nach den üblichen 12 Grundstoff-Lektionen + Zusatzstoff
        //    Klasse B (bitte fachlich prüfen!) – alles im Adminpanel änderbar,
        //    Änderungen erzeugen dort automatisch neue Versionen.
        if (!await db.CurriculumItems.AnyAsync())
        {
            var now = DateTime.UtcNow;
            var reihenfolge = 0;
            CurriculumItem Thema(string abschnitt, string titel, params LicenseClass[] klassen) => new()
            {
                Id = Guid.NewGuid(), ItemKey = Guid.NewGuid(), Version = 1,
                ValidFromUtc = now, Section = abschnitt, Title = titel,
                IsActive = true, SortOrder = (reihenfolge += 10),
                CreatedAtUtc = now, UpdatedAtUtc = now,
                Classes = [.. klassen.Select(k => new CurriculumItemClass { LicenseClassId = k.Id })],
            };

            // Grundstoff: KEINE Klassen-Zuordnung = gilt für alle Klassen.
            string[] grundstoff =
            [
                "Persönliche Voraussetzungen",
                "Risikofaktor Mensch",
                "Rechtliche Rahmenbedingungen",
                "Straßenverkehrssystem und seine Nutzung",
                "Vorfahrt und Verkehrsregelungen",
                "Verkehrszeichen und Verkehrseinrichtungen",
                "Andere Teilnehmer im Straßenverkehr",
                "Geschwindigkeit, Abstand und umweltschonende Fahrweise",
                "Verkehrsverhalten bei Fahrmanövern, Verkehrsbeobachtung",
                "Ruhender Verkehr",
                "Verhalten in besonderen Situationen, Folgen von Verstößen",
                "Lebenslanges Lernen / Folgen für die Fahrerlaubnis",
            ];
            db.CurriculumItems.AddRange(grundstoff.Select(t => Thema("Theorie-Grundstoff", t)));

            // Zusatzstoff Klasse B: gilt nur für B (Klassen-Zuordnung gesetzt).
            var klasseB = await db.LicenseClasses.FirstOrDefaultAsync(k => k.Code == "B");
            if (klasseB is not null)
            {
                db.CurriculumItems.AddRange(
                    Thema("Theorie-Zusatzstoff", "Technische Bedingungen, umweltbewusster Umgang (Pkw)", klasseB),
                    Thema("Theorie-Zusatzstoff", "Fahren mit Solo-Kraftfahrzeugen und Zügen (Pkw)", klasseB));
            }

            await db.SaveChangesAsync();
            logger.LogInformation("Beispiel-Theoriethemen angelegt (Startwerte, im Adminpanel änderbar).");
        }

        // 5. Ersten Admin anlegen – nur, wenn es noch gar keine Benutzer gibt.
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
