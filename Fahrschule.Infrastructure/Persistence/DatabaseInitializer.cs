using Fahrschule.Domain.Entities;
using Fahrschule.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Fahrschule.Infrastructure.Persistence;

/// <summary>
/// Brings the database into a usable state at startup:
/// 1. Apply pending migrations (keep the schema up to date).
/// 2. Create the three roles if they are missing.
/// 3. Seed sample data (licence classes, theory topics) once.
/// 4. Create the first admin once ("seed") - credentials come from
///    configuration (appsettings/environment variables, NEVER hard-coded).
///
/// The admin seed only runs while no user exists at all - after that it
/// deactivates itself (see KONZEPT, "Erster Admin per Seed").
/// </summary>
public static class DatabaseInitializer
{
    public static async Task InitializeAsync(IServiceProvider services)
    {
        // A "scope" is a short-lived service container. DbContext & co. are
        // meant to live per request; at startup there is no request, so we
        // create our own scope (typical dependency injection pattern).
        using var scope = services.CreateScope();
        var provider = scope.ServiceProvider;

        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseInitializer");
        var db = provider.GetRequiredService<FahrschuleDbContext>();

        // 1. Apply migrations - also creates the database on the very first start.
        await db.Database.MigrateAsync();

        // 2. Create roles if missing (idempotent: starting twice does no harm).
        var roleManager = provider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        foreach (var role in Roles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole<Guid>(role));
                logger.LogInformation("Rolle {Role} angelegt.", role);
            }
        }

        // 3. Seed sample licence classes - only if the table is empty.
        //    These are STARTING VALUES (common classes + minimum ages, as
        //    researched in 2026 - please verify!). Everything is editable in
        //    the admin panel afterwards; nothing is fixed in code.
        if (!await db.LicenseClasses.AnyAsync())
        {
            var now = DateTime.UtcNow;
            // Training-plan target numbers per class (theory double lessons +
            // special drives Überland/Autobahn/Nacht). Legal starting values; the
            // owner edits them in the admin panel. Only filled where they are
            // legally well-defined (class B); others start at 0 (please verify).
            LicenseClass NewClass(string code, string description, int? age, string? requirements, int sortOrder,
                int theoryDoubleLessons = 0, int overland = 0, int highway = 0, int night = 0) => new()
            {
                Id = Guid.NewGuid(), Code = code, Description = description,
                MinimumAge = age, Requirements = requirements,
                IsActive = true, SortOrder = sortOrder, CreatedAtUtc = now, UpdatedAtUtc = now,
                RequiredTheoryDoubleLessons = theoryDoubleLessons,
                RequiredSpecialDrivesOverland = overland,
                RequiredSpecialDrivesHighway = highway,
                RequiredSpecialDrivesNight = night,
            };

            db.LicenseClasses.AddRange(
                NewClass("AM", "Moped/Roller bis 45 km/h", 15, "Mindestalter je nach Bundesland 15 oder 16 Jahre", 10),
                NewClass("A1", "Leichtkrafträder bis 125 cm³", 16, null, 20),
                NewClass("A2", "Krafträder bis 35 kW", 18, "Direkteinstieg oder Aufstieg von A1 nach 2 Jahren", 30),
                NewClass("A", "Krafträder unbeschränkt", 24, "Direkteinstieg ab 24; Aufstieg von A2 nach 2 Jahren ab 20", 40),
                // Class B (§ 5 FahrSchAusbO): 2 Zusatzstoff double lessons, special
                // drives Überland 5 / Autobahn 4 / Nacht 3.
                NewClass("B", "Pkw bis 3,5 t", 17, "Mit 17 nur begleitetes Fahren (BF17); allein ab 18", 50,
                    theoryDoubleLessons: 2, overland: 5, highway: 4, night: 3),
                NewClass("BE", "Pkw mit Anhänger über 750 kg", 17, "Vorbesitz Klasse B erforderlich", 60),
                NewClass("B96", "Pkw mit Anhänger (Zug bis 4,25 t)", 17, "Erweiterung zu Klasse B (nur Schulung, keine Prüfung)", 70),
                NewClass("L", "Land-/forstwirtschaftliche Zugmaschinen bis 40 km/h", 16, null, 80),
                NewClass("T", "Land-/forstwirtschaftliche Zugmaschinen bis 60 km/h", 16, null, 90));

            await db.SaveChangesAsync();
            logger.LogInformation("Beispiel-Führerscheinklassen angelegt (Startwerte, im Adminpanel änderbar).");
        }

        // 4. Seed sample theory topics - only if the table is empty.
        //    STARTING VALUES based on the usual 12 basic theory lessons plus
        //    class B supplementary lessons (please verify!) - all editable in
        //    the admin panel; edits there automatically create new versions.
        if (!await db.CurriculumItems.AnyAsync())
        {
            var now = DateTime.UtcNow;
            var sortOrder = 0;
            CurriculumItem Topic(string section, string title, params LicenseClass[] classes) => new()
            {
                Id = Guid.NewGuid(), ItemKey = Guid.NewGuid(), Version = 1,
                ValidFromUtc = now, Section = section, Title = title,
                IsActive = true, SortOrder = (sortOrder += 10),
                CreatedAtUtc = now, UpdatedAtUtc = now,
                Classes = [.. classes.Select(c => new CurriculumItemClass { LicenseClassId = c.Id })],
            };

            // Basic material: NO class assignment = applies to all classes.
            string[] basicTopics =
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
            db.CurriculumItems.AddRange(basicTopics.Select(t => Topic("Theorie-Grundstoff", t)));

            // Class B supplementary material: applies to B only (assignment set).
            var classB = await db.LicenseClasses.FirstOrDefaultAsync(c => c.Code == "B");
            if (classB is not null)
            {
                db.CurriculumItems.AddRange(
                    Topic("Theorie-Zusatzstoff", "Technische Bedingungen, umweltbewusster Umgang (Pkw)", classB),
                    Topic("Theorie-Zusatzstoff", "Fahren mit Solo-Kraftfahrzeugen und Zügen (Pkw)", classB));
            }

            await db.SaveChangesAsync();
            logger.LogInformation("Beispiel-Theoriethemen angelegt (Startwerte, im Adminpanel änderbar).");
        }

        // 5. Seed sample document catalogue entries - only if the table is empty.
        //    STARTING VALUES: the common standard documents (apply to all
        //    classes). Data minimisation: only the STATUS is tracked later,
        //    never the documents themselves. All editable in the admin panel.
        if (!await db.DocumentCatalogItems.AnyAsync())
        {
            var now = DateTime.UtcNow;
            var sortOrder = 0;
            DocumentCatalogItem Document(string name, bool expiryRequired, string? note = null) => new()
            {
                Id = Guid.NewGuid(), Name = name, Note = note,
                ExpiryDateRequired = expiryRequired, IsActive = true,
                SortOrder = (sortOrder += 10), CreatedAtUtc = now, UpdatedAtUtc = now,
                // No class assignment = required for all classes.
            };

            db.DocumentCatalogItems.AddRange(
                Document("Sehtest", expiryRequired: false),
                Document("Erste-Hilfe-Bescheinigung", expiryRequired: false),
                Document("Biometrisches Passbild", expiryRequired: false),
                Document("Antrag bei der Führerscheinstelle", expiryRequired: true,
                    note: "Vom Amt ausgestellt; Ablaufdatum verpflichtend eintragen"),
                Document("Personalausweis oder Reisepass (Kopie geprüft)", expiryRequired: false));

            await db.SaveChangesAsync();
            logger.LogInformation("Beispiel-Unterlagen angelegt (Startwerte, im Adminpanel änderbar).");
        }

        // 6. Create the first admin - only while no user exists at all.
        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        if (await userManager.Users.AnyAsync())
        {
            return; // The seed has done its job and stays silent from now on.
        }

        var config = provider.GetRequiredService<IConfiguration>();
        // Login is by user name (no e-mail on accounts). Accept the new
        // Seed:Admin:UserName and fall back to the former Seed:Admin:Email key
        // so existing configurations keep working.
        var userName = config["Seed:Admin:UserName"] ?? config["Seed:Admin:Email"];
        var password = config["Seed:Admin:Password"];
        var displayName = config["Seed:Admin:DisplayName"] ?? "Inhaber";

        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning(
                "Kein Admin-Seed konfiguriert (Seed:Admin:UserName / Seed:Admin:Password). " +
                "Ohne ersten Admin ist keine Anmeldung möglich.");
            return;
        }

        var admin = new ApplicationUser
        {
            UserName = userName, // login name (no e-mail stored on accounts)
            DisplayName = displayName,
            CreatedAtUtc = DateTime.UtcNow,
            // The seed password lives in a configuration file and therefore
            // counts as "temporary": the first sign-in forces a new password.
            MustChangePassword = true,
        };

        var result = await userManager.CreateAsync(admin, password);
        if (!result.Succeeded)
        {
            var errors = string.Join("; ", result.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"Admin-Seed fehlgeschlagen: {errors}");
        }

        await userManager.AddToRoleAsync(admin, Roles.Admin);
        logger.LogInformation("Erster Admin {UserName} wurde angelegt (Passwort muss beim ersten Login geändert werden).", userName);
    }
}
