using Fahrschule.Domain.Entities;
using Fahrschule.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Fahrschule.Infrastructure.Persistence;

/// <summary>
/// Der DbContext ist die Brücke zwischen C#-Klassen und der PostgreSQL-Datenbank.
///
/// Web-typisches Konzept "ORM" (Object-Relational Mapper): Wir arbeiten im Code
/// mit normalen Objekten (z. B. AuditLog), und EF Core übersetzt das in
/// SQL-Befehle. Tabellen entstehen über "Migrationen" – versionierte
/// Änderungsschritte am Datenbankschema (siehe Ordner Migrations/).
///
/// Wir erben von IdentityDbContext, damit die fertigen Identity-Tabellen
/// (Benutzer, Rollen, …) gleich mit dabei sind.
///
/// Unity-Brücke: grob vergleichbar mit einem SaveGame-System, das den Zustand
/// der Objekte automatisch in eine Datei (hier: Datenbank) schreibt und lädt.
/// </summary>
public class FahrschuleDbContext(DbContextOptions<FahrschuleDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Setting> Settings => Set<Setting>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<LicenseClass> LicenseClasses => Set<LicenseClass>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Wichtig: zuerst die Identity-Konfiguration übernehmen.
        base.OnModelCreating(builder);

        builder.Entity<AuditLog>(log =>
        {
            // Das Audit-Log wird oft nach Zeitraum gefiltert → Index beschleunigt das.
            log.HasIndex(x => x.TimestampUtc);
            log.HasIndex(x => new { x.EntityType, x.EntityId });
            log.Property(x => x.Action).HasMaxLength(100);
            log.Property(x => x.EntityType).HasMaxLength(100);
            log.Property(x => x.EntityId).HasMaxLength(100);
            log.Property(x => x.UserName).HasMaxLength(200);
        });

        builder.Entity<Setting>(setting =>
        {
            // Der Schlüssel selbst ist der Primärschlüssel ("Erinnerung.VorlaufMinuten" …).
            setting.HasKey(x => x.Key);
            setting.Property(x => x.Key).HasMaxLength(200);
        });

        builder.Entity<LicenseClass>(klasse =>
        {
            klasse.Property(x => x.Code).HasMaxLength(10);
            klasse.Property(x => x.Description).HasMaxLength(300);
            klasse.Property(x => x.Requirements).HasMaxLength(1000);

            // Kürzel eindeutig – aber nur unter den NICHT gelöschten Klassen
            // (eine gelöschte "B" darf ein neues "B" nicht blockieren).
            klasse.HasIndex(x => x.Code).IsUnique().HasFilter("\"IsDeleted\" = false");

            // Globaler Filter: Soft-gelöschte Datensätze sind für alle normalen
            // Abfragen unsichtbar (Projektregel 7) – nur der spätere
            // Wiederherstellen-/Aufbewahrungs-Code hebt den Filter gezielt auf.
            klasse.HasQueryFilter(x => !x.IsDeleted);

            // Optimistische Nebenläufigkeit: PostgreSQL führt pro Zeile die
            // Systemspalte "xmin" (ändert sich bei jedem Schreiben). EF nutzt
            // sie als Versionsmarke gegen gegenseitiges Überschreiben.
            klasse.Property<uint>("xmin").IsRowVersion();
        });

        builder.Entity<RefreshToken>(token =>
        {
            // Beim Refresh suchen wir das Token über seinen Hash → eindeutiger Index.
            token.HasIndex(x => x.TokenHash).IsUnique();
            token.Property(x => x.TokenHash).HasMaxLength(128);
            token.Property(x => x.ReplacedByTokenHash).HasMaxLength(128);

            // Wird ein Benutzer endgültig entfernt, verschwinden auch seine Tokens.
            token.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
