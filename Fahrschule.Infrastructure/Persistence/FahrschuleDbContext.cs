using Fahrschule.Domain.Entities;
using Fahrschule.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Fahrschule.Infrastructure.Persistence;

/// <summary>
/// The DbContext is the bridge between C# classes and the PostgreSQL database.
///
/// Common web concept "ORM" (Object-Relational Mapper): we work with plain
/// objects in code (e.g. AuditLog) and EF Core translates that into SQL.
/// Tables are created via "migrations" - versioned change steps of the
/// database schema (see the Migrations/ folder).
///
/// We inherit from IdentityDbContext so the ready-made Identity tables
/// (users, roles, ...) are included automatically.
/// </summary>
public class FahrschuleDbContext(DbContextOptions<FahrschuleDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Setting> Settings => Set<Setting>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<LicenseClass> LicenseClasses => Set<LicenseClass>();
    public DbSet<CurriculumItem> CurriculumItems => Set<CurriculumItem>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Important: apply the Identity configuration first.
        base.OnModelCreating(builder);

        builder.Entity<AuditLog>(log =>
        {
            // The audit log is often filtered by time range → index speeds that up.
            log.HasIndex(x => x.TimestampUtc);
            log.HasIndex(x => new { x.EntityType, x.EntityId });
            log.Property(x => x.Action).HasMaxLength(100);
            log.Property(x => x.EntityType).HasMaxLength(100);
            log.Property(x => x.EntityId).HasMaxLength(100);
            log.Property(x => x.UserName).HasMaxLength(200);
        });

        builder.Entity<Setting>(setting =>
        {
            // The key itself is the primary key ("Erinnerung.VorlaufMinuten" ...).
            setting.HasKey(x => x.Key);
            setting.Property(x => x.Key).HasMaxLength(200);
        });

        builder.Entity<LicenseClass>(licenseClass =>
        {
            licenseClass.Property(x => x.Code).HasMaxLength(10);
            licenseClass.Property(x => x.Description).HasMaxLength(300);
            licenseClass.Property(x => x.Requirements).HasMaxLength(1000);

            // Code must be unique - but only among NON-deleted classes
            // (a deleted "B" must not block creating a new "B").
            licenseClass.HasIndex(x => x.Code).IsUnique().HasFilter("\"IsDeleted\" = false");

            // Global filter: soft-deleted records are invisible to all normal
            // queries (project rule 7) - only the future restore/retention
            // code will bypass the filter explicitly.
            licenseClass.HasQueryFilter(x => !x.IsDeleted);

            // Optimistic concurrency: PostgreSQL maintains the per-row system
            // column "xmin" (changes on every write). EF uses it as a version
            // marker to prevent users overwriting each other's changes.
            licenseClass.Property<uint>("xmin").IsRowVersion();
        });

        builder.Entity<CurriculumItem>(item =>
        {
            item.Property(x => x.Section).HasMaxLength(100);
            item.Property(x => x.Title).HasMaxLength(300);

            // Most frequent query: "current version per item key" → matching index.
            item.HasIndex(x => new { x.ItemKey, x.Version }).IsUnique();
            item.HasIndex(x => x.Section);

            item.HasQueryFilter(x => !x.IsDeleted);
            item.Property<uint>("xmin").IsRowVersion();
        });

        builder.Entity<CurriculumItemClass>(link =>
        {
            // Composite key: each item+class combination only once.
            link.HasKey(x => new { x.CurriculumItemId, x.LicenseClassId });

            link.HasOne(x => x.CurriculumItem)
                .WithMany(x => x.Classes)
                .HasForeignKey(x => x.CurriculumItemId)
                .OnDelete(DeleteBehavior.Cascade);

            // Licence classes are only ever soft-deleted - a hard delete must
            // never silently drag link rows along (Restrict = the database
            // refuses).
            link.HasOne(x => x.LicenseClass)
                .WithMany()
                .HasForeignKey(x => x.LicenseClassId)
                .OnDelete(DeleteBehavior.Restrict);

            // Important: CurriculumItem filters deleted rows globally - EF
            // requires dependent tables to mirror the same filter (otherwise
            // there could be links to "invisible" items).
            link.HasQueryFilter(x => !x.CurriculumItem!.IsDeleted);
        });

        builder.Entity<RefreshToken>(token =>
        {
            // The refresh endpoint looks tokens up by hash → unique index.
            token.HasIndex(x => x.TokenHash).IsUnique();
            token.Property(x => x.TokenHash).HasMaxLength(128);
            token.Property(x => x.ReplacedByTokenHash).HasMaxLength(128);

            // If a user is permanently removed, their tokens go with them.
            token.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
