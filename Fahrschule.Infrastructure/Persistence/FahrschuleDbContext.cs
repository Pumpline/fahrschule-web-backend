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
    public DbSet<DocumentCatalogItem> DocumentCatalogItems => Set<DocumentCatalogItem>();
    public DbSet<Student> Students => Set<Student>();
    public DbSet<DocumentChecklistItem> DocumentChecklistItems => Set<DocumentChecklistItem>();
    public DbSet<StudentProgressItem> StudentProgressItems => Set<StudentProgressItem>();

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

        builder.Entity<DocumentCatalogItem>(doc =>
        {
            doc.Property(x => x.Name).HasMaxLength(200);
            doc.Property(x => x.Note).HasMaxLength(1000);

            doc.HasIndex(x => x.SortOrder);
            doc.HasQueryFilter(x => !x.IsDeleted);
            doc.Property<uint>("xmin").IsRowVersion();
        });

        builder.Entity<DocumentCatalogItemClass>(link =>
        {
            link.HasKey(x => new { x.DocumentCatalogItemId, x.LicenseClassId });

            link.HasOne(x => x.DocumentCatalogItem)
                .WithMany(x => x.Classes)
                .HasForeignKey(x => x.DocumentCatalogItemId)
                .OnDelete(DeleteBehavior.Cascade);

            // Licence classes are only ever soft-deleted - never let a hard
            // delete silently drag link rows along (Restrict).
            link.HasOne(x => x.LicenseClass)
                .WithMany()
                .HasForeignKey(x => x.LicenseClassId)
                .OnDelete(DeleteBehavior.Restrict);

            // Mirror the parent's soft-delete filter (EF requires this).
            link.HasQueryFilter(x => !x.DocumentCatalogItem!.IsDeleted);
        });

        builder.Entity<Student>(student =>
        {
            student.Property(x => x.FirstName).HasMaxLength(100);
            student.Property(x => x.LastName).HasMaxLength(100);
            student.Property(x => x.Email).HasMaxLength(256);
            student.Property(x => x.Phone).HasMaxLength(50);
            student.Property(x => x.Street).HasMaxLength(200);
            student.Property(x => x.PostalCode).HasMaxLength(20);
            student.Property(x => x.City).HasMaxLength(100);
            student.Property(x => x.Notes).HasMaxLength(2000);

            student.HasIndex(x => x.LastName);
            student.HasQueryFilter(x => !x.IsDeleted);
            student.Property<uint>("xmin").IsRowVersion();
        });

        builder.Entity<StudentLicenseClass>(link =>
        {
            // Composite key: a student has each class at most once.
            link.HasKey(x => new { x.StudentId, x.LicenseClassId });
            link.Property(x => x.Phase).HasConversion<string>(); // store the enum name, readable in the DB

            link.HasOne(x => x.Student)
                .WithMany(x => x.LicenseClasses)
                .HasForeignKey(x => x.StudentId)
                .OnDelete(DeleteBehavior.Cascade);

            // Licence classes are only ever soft-deleted - never let a hard
            // delete drag a student's registrations along (Restrict).
            link.HasOne(x => x.LicenseClass)
                .WithMany()
                .HasForeignKey(x => x.LicenseClassId)
                .OnDelete(DeleteBehavior.Restrict);

            // Mirror the student's soft-delete filter (EF requires this).
            link.HasQueryFilter(x => !x.Student!.IsDeleted);
        });

        builder.Entity<DocumentChecklistItem>(item =>
        {
            // One status row per student + catalogue item.
            item.HasKey(x => new { x.StudentId, x.DocumentCatalogItemId });

            item.HasOne(x => x.Student)
                .WithMany()
                .HasForeignKey(x => x.StudentId)
                .OnDelete(DeleteBehavior.Cascade);

            // The catalogue item is only soft-deleted - never let a hard delete
            // drag a student's status along (Restrict).
            item.HasOne(x => x.DocumentCatalogItem)
                .WithMany()
                .HasForeignKey(x => x.DocumentCatalogItemId)
                .OnDelete(DeleteBehavior.Restrict);

            // Mirror the soft-delete filters of both referenced entities
            // (EF requires dependents to match the principals' query filters).
            item.HasQueryFilter(x => !x.Student!.IsDeleted && !x.DocumentCatalogItem!.IsDeleted);
        });

        builder.Entity<StudentProgressItem>(item =>
        {
            item.Property(x => x.Section).HasMaxLength(100);
            item.Property(x => x.Title).HasMaxLength(300);
            item.Property(x => x.Note).HasMaxLength(1000);

            // The checklist is always loaded for one student → index by student.
            item.HasIndex(x => x.StudentId);

            item.HasOne(x => x.Student)
                .WithMany()
                .HasForeignKey(x => x.StudentId)
                .OnDelete(DeleteBehavior.Cascade);

            // Mirror the student's soft-delete filter (EF requires dependents to
            // match the principal's query filter).
            item.HasQueryFilter(x => !x.Student!.IsDeleted);
        });

        builder.Entity<StudentProgressItemClass>(link =>
        {
            link.HasKey(x => new { x.StudentProgressItemId, x.LicenseClassId });

            link.HasOne(x => x.StudentProgressItem)
                .WithMany(x => x.Classes)
                .HasForeignKey(x => x.StudentProgressItemId)
                .OnDelete(DeleteBehavior.Cascade);

            // Licence classes are only ever soft-deleted - never let a hard
            // delete drag a student's progress links along (Restrict).
            link.HasOne(x => x.LicenseClass)
                .WithMany()
                .HasForeignKey(x => x.LicenseClassId)
                .OnDelete(DeleteBehavior.Restrict);

            // Mirror the soft-delete filter of the owning progress item's student.
            link.HasQueryFilter(x => !x.StudentProgressItem!.Student!.IsDeleted);
        });

        builder.Entity<StudentProgressEntry>(entry =>
        {
            entry.Property(x => x.Note).HasMaxLength(1000);

            entry.HasOne(x => x.StudentProgressItem)
                .WithMany(x => x.Entries)
                .HasForeignKey(x => x.StudentProgressItemId)
                .OnDelete(DeleteBehavior.Cascade);

            // Mirror the soft-delete filter of the owning progress item's student.
            entry.HasQueryFilter(x => !x.StudentProgressItem!.Student!.IsDeleted);
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
