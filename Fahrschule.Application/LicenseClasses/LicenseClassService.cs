using System.Text.Json;
using Fahrschule.Application.Audit;
using Fahrschule.Application.Common;
using Fahrschule.Contracts.LicenseClasses;
using Fahrschule.Domain.Entities;
using Fahrschule.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fahrschule.Application.LicenseClasses;

/// <summary>Who performs a change - for the audit log (taken from the token).</summary>
public record Actor(Guid UserId, string UserName);

public interface ILicenseClassService
{
    Task<List<LicenseClassDto>> GetAllAsync(CancellationToken ct = default);
    Task<LicenseClassDto> CreateAsync(CreateLicenseClassRequest request, Actor actor, CancellationToken ct = default);
    Task<LicenseClassDto> UpdateAsync(Guid id, UpdateLicenseClassRequest request, Actor actor, CancellationToken ct = default);
    Task DeleteAsync(Guid id, Actor actor, CancellationToken ct = default);
}

/// <summary>
/// Business logic for maintaining licence classes (admin panel).
///
/// Every change is written to the audit log (who/when/before/after - GDPR
/// principle; no personal data here, but consistent traceability for all
/// master data changes). Deleting is always "soft" (project rule 7).
/// </summary>
public class LicenseClassService(FahrschuleDbContext db, IAuditWriter auditWriter) : ILicenseClassService
{
    public async Task<List<LicenseClassDto>> GetAllAsync(CancellationToken ct = default)
    {
        // Deleted classes are filtered out automatically by the global query filter.
        return await db.LicenseClasses
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Code)
            .Select(x => new LicenseClassDto
            {
                Id = x.Id,
                Code = x.Code,
                Description = x.Description,
                MinimumAge = x.MinimumAge,
                Requirements = x.Requirements,
                IsActive = x.IsActive,
                SortOrder = x.SortOrder,
                RequiredTheoryDoubleLessons = x.RequiredTheoryDoubleLessons,
                RequiredSpecialDrivesOverland = x.RequiredSpecialDrivesOverland,
                RequiredSpecialDrivesHighway = x.RequiredSpecialDrivesHighway,
                RequiredSpecialDrivesNight = x.RequiredSpecialDrivesNight,
                // xmin = PostgreSQL system column used as version marker (see DTO).
                Version = EF.Property<uint>(x, "xmin"),
            })
            .ToListAsync(ct);
    }

    public async Task<LicenseClassDto> CreateAsync(CreateLicenseClassRequest request, Actor actor, CancellationToken ct = default)
    {
        var code = LicenseClassRules.NormalizeCode(request.Code);
        await ValidateAsync(code, request.MinimumAge, existingId: null, ct);

        var now = DateTime.UtcNow;
        var entity = new LicenseClass
        {
            Id = Guid.NewGuid(),
            Code = code,
            Description = request.Description.Trim(),
            MinimumAge = request.MinimumAge,
            Requirements = NullIfEmpty(request.Requirements),
            IsActive = request.IsActive,
            SortOrder = request.SortOrder,
            RequiredTheoryDoubleLessons = Math.Max(0, request.RequiredTheoryDoubleLessons),
            RequiredSpecialDrivesOverland = Math.Max(0, request.RequiredSpecialDrivesOverland),
            RequiredSpecialDrivesHighway = Math.Max(0, request.RequiredSpecialDrivesHighway),
            RequiredSpecialDrivesNight = Math.Max(0, request.RequiredSpecialDrivesNight),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        db.LicenseClasses.Add(entity);
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(actor.UserId, actor.UserName, "Angelegt",
            "Führerscheinklasse", entity.Code, newValuesJson: Snapshot(entity), cancellationToken: ct);

        return ToDto(entity);
    }

    public async Task<LicenseClassDto> UpdateAsync(Guid id, UpdateLicenseClassRequest request, Actor actor, CancellationToken ct = default)
    {
        var entity = await db.LicenseClasses.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException("Diese Führerscheinklasse wurde nicht gefunden. Vielleicht wurde sie gerade gelöscht – bitte Liste neu laden.");

        var code = LicenseClassRules.NormalizeCode(request.Code);
        await ValidateAsync(code, request.MinimumAge, existingId: id, ct);

        var oldSnapshot = Snapshot(entity);

        entity.Code = code;
        entity.Description = request.Description.Trim();
        entity.MinimumAge = request.MinimumAge;
        entity.Requirements = NullIfEmpty(request.Requirements);
        entity.IsActive = request.IsActive;
        entity.SortOrder = request.SortOrder;
        entity.RequiredTheoryDoubleLessons = Math.Max(0, request.RequiredTheoryDoubleLessons);
        entity.RequiredSpecialDrivesOverland = Math.Max(0, request.RequiredSpecialDrivesOverland);
        entity.RequiredSpecialDrivesHighway = Math.Max(0, request.RequiredSpecialDrivesHighway);
        entity.RequiredSpecialDrivesNight = Math.Max(0, request.RequiredSpecialDrivesNight);
        entity.UpdatedAtUtc = DateTime.UtcNow;

        // Optimistic concurrency: we tell EF which version the editor started
        // from. If it no longer matches the database, SaveChanges throws a
        // conflict (→ 409 with an understandable message, see
        // ExceptionHandlingMiddleware) instead of overwriting someone else's work.
        db.Entry(entity).Property<uint>("xmin").OriginalValue = request.Version;

        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(actor.UserId, actor.UserName, "Geändert",
            "Führerscheinklasse", entity.Code, oldSnapshot, Snapshot(entity), ct);

        return ToDto(entity);
    }

    public async Task DeleteAsync(Guid id, Actor actor, CancellationToken ct = default)
    {
        var entity = await db.LicenseClasses.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException("Diese Führerscheinklasse wurde nicht gefunden. Vielleicht wurde sie bereits gelöscht.");

        // Soft delete: flag only - restorable; actual removal is done later
        // by the retention job (project rule 7).
        entity.IsDeleted = true;
        entity.DeletedAtUtc = DateTime.UtcNow;
        entity.DeletedByUserId = actor.UserId;
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(actor.UserId, actor.UserName, "Gelöscht",
            "Führerscheinklasse", entity.Code, oldValuesJson: Snapshot(entity), cancellationToken: ct);
    }

    /// <summary>Form rules + uniqueness of the code.</summary>
    private async Task ValidateAsync(string code, int? minimumAge, Guid? existingId, CancellationToken ct)
    {
        var errors = LicenseClassRules.Validate(code, minimumAge);
        if (errors.Count > 0)
        {
            throw new AppValidationException(string.Join(" ", errors));
        }

        var duplicate = await db.LicenseClasses
            .AnyAsync(x => x.Code == code && (existingId == null || x.Id != existingId), ct);
        if (duplicate)
        {
            throw new AppValidationException(
                $"Es gibt bereits eine Führerscheinklasse mit dem Kürzel „{code}“. " +
                "Bitte ein anderes Kürzel wählen oder die bestehende Klasse bearbeiten.");
        }
    }

    private LicenseClassDto ToDto(LicenseClass entity) => new()
    {
        Id = entity.Id,
        Code = entity.Code,
        Description = entity.Description,
        MinimumAge = entity.MinimumAge,
        Requirements = entity.Requirements,
        IsActive = entity.IsActive,
        SortOrder = entity.SortOrder,
        RequiredTheoryDoubleLessons = entity.RequiredTheoryDoubleLessons,
        RequiredSpecialDrivesOverland = entity.RequiredSpecialDrivesOverland,
        RequiredSpecialDrivesHighway = entity.RequiredSpecialDrivesHighway,
        RequiredSpecialDrivesNight = entity.RequiredSpecialDrivesNight,
        // After SaveChanges PostgreSQL returns the new version marker.
        Version = db.Entry(entity).Property<uint>("xmin").CurrentValue,
    };

    /// <summary>State of the class as JSON for the audit log (before/after).</summary>
    private static string Snapshot(LicenseClass x) => JsonSerializer.Serialize(new
    {
        x.Code, x.Description, x.MinimumAge, x.Requirements, x.IsActive, x.SortOrder,
        x.RequiredTheoryDoubleLessons,
        x.RequiredSpecialDrivesOverland, x.RequiredSpecialDrivesHighway, x.RequiredSpecialDrivesNight,
    });

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
