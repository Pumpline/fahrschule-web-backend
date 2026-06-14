using System.Text.Json;
using Fahrschule.Application.Audit;
using Fahrschule.Application.Common;
using Fahrschule.Application.LicenseClasses;
using Fahrschule.Contracts.Curriculum;
using Fahrschule.Domain.Entities;
using Fahrschule.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fahrschule.Application.Curriculum;

public interface ICurriculumItemService
{
    /// <summary>All CURRENTLY valid items (latest version per item key).</summary>
    Task<List<CurriculumItemDto>> GetCurrentAsync(string? section = null, CancellationToken ct = default);
    Task<CurriculumItemDto> CreateAsync(CreateCurriculumItemRequest request, Actor actor, CancellationToken ct = default);
    Task<CurriculumItemDto> UpdateAsync(Guid id, UpdateCurriculumItemRequest request, Actor actor, CancellationToken ct = default);
    Task DeleteAsync(Guid id, Actor actor, CancellationToken ct = default);
}

/// <summary>
/// Business logic for maintaining curriculum items (admin panel).
///
/// The core is VERSIONING (KONZEPT 3.3a): content changes create a new row
/// with version+1 and flag the old one as superseded - nothing is ever
/// destroyed. Student checklists (coming in step 4) will reference the
/// version that applied at their registration time.
/// </summary>
public class CurriculumItemService(FahrschuleDbContext db, IAuditWriter auditWriter) : ICurriculumItemService
{
    public async Task<List<CurriculumItemDto>> GetCurrentAsync(string? section = null, CancellationToken ct = default)
    {
        var query = db.CurriculumItems
            .Where(x => x.SupersededAtUtc == null); // current versions only

        if (!string.IsNullOrWhiteSpace(section))
        {
            query = query.Where(x => x.Section == section);
        }

        var items = await query
            .Include(x => x.Classes).ThenInclude(c => c.LicenseClass)
            .OrderBy(x => x.Section).ThenBy(x => x.SortOrder).ThenBy(x => x.Title)
            .ToListAsync(ct);

        return items.Select(x => ToDto(x)).ToList();
    }

    public async Task<CurriculumItemDto> CreateAsync(CreateCurriculumItemRequest request, Actor actor, CancellationToken ct = default)
    {
        var title = CurriculumRules.NormalizeTitle(request.Title);
        ThrowIfInvalid(title, request.RequiredCount);
        await EnsureClassesExistAsync(request.ClassIds, ct);

        var now = DateTime.UtcNow;
        var entity = new CurriculumItem
        {
            Id = Guid.NewGuid(),
            ItemKey = Guid.NewGuid(), // new fixed identifier - stays the same across versions
            Version = 1,
            ValidFromUtc = now,
            Section = request.Section.Trim(),
            Title = title,
            RequiredCount = request.RequiredCount,
            IsActive = request.IsActive,
            SortOrder = request.SortOrder,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Classes = [.. request.ClassIds.Distinct().Select(id => new CurriculumItemClass { LicenseClassId = id })],
        };

        db.CurriculumItems.Add(entity);
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(actor.UserId, actor.UserName, "Angelegt",
            "Ausbildungsplan-Punkt", title, newValuesJson: await SnapshotAsync(entity, ct), cancellationToken: ct);

        return await ReloadDtoAsync(entity.Id, ct);
    }

    public async Task<CurriculumItemDto> UpdateAsync(Guid id, UpdateCurriculumItemRequest request, Actor actor, CancellationToken ct = default)
    {
        var entity = await db.CurriculumItems
            .Include(x => x.Classes)
            .FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException("Dieser Punkt wurde nicht gefunden. Bitte die Liste neu laden.");

        if (entity.SupersededAtUtc is not null)
        {
            // Someone created a newer version in parallel.
            throw new AppValidationException(
                "Von diesem Punkt gibt es inzwischen eine neuere Version. Bitte die Liste neu laden und dort weiterarbeiten.");
        }

        var title = CurriculumRules.NormalizeTitle(request.Title);
        ThrowIfInvalid(title, request.RequiredCount);
        await EnsureClassesExistAsync(request.ClassIds, ct);

        var oldSnapshot = await SnapshotAsync(entity, ct);
        var oldClassIds = entity.Classes.Select(c => c.LicenseClassId).ToList();
        var now = DateTime.UtcNow;

        // Apply the editor's version marker - protects against mutual overwrites.
        db.Entry(entity).Property<uint>("xmin").OriginalValue = request.RowVersion;

        Guid resultId;
        string auditAction;

        var contentChanged = CurriculumRules.NeedsNewVersion(
            entity.Title, title, entity.RequiredCount, request.RequiredCount, oldClassIds, request.ClassIds);

        if (contentChanged && request.AsNewVersion)
        {
            // The editor chose "new version": supersede the old one, add a new row.
            // Existing student snapshots keep pointing at the old version.
            entity.SupersededAtUtc = now;

            var newVersion = new CurriculumItem
            {
                Id = Guid.NewGuid(),
                ItemKey = entity.ItemKey,
                Version = entity.Version + 1,
                ValidFromUtc = now,
                Section = entity.Section,
                Title = title,
                RequiredCount = request.RequiredCount,
                IsActive = request.IsActive,
                SortOrder = request.SortOrder,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                Classes = [.. request.ClassIds.Distinct().Select(cid => new CurriculumItemClass { LicenseClassId = cid })],
            };
            db.CurriculumItems.Add(newVersion);
            resultId = newVersion.Id;
            auditAction = $"Geändert (neue Version {newVersion.Version})";
        }
        else
        {
            // Correct the SAME version in place. For an organisational-only change
            // (active/sort) this is the normal path; for a content change the
            // editor explicitly chose "correct" (applies retroactively to everyone).
            entity.Title = title;
            entity.RequiredCount = request.RequiredCount;
            entity.IsActive = request.IsActive;
            entity.SortOrder = request.SortOrder;
            entity.UpdatedAtUtc = now;

            // Sync the class assignment (M:N): drop the old links, add the new set.
            entity.Classes.Clear();
            foreach (var classId in request.ClassIds.Distinct())
            {
                entity.Classes.Add(new CurriculumItemClass { LicenseClassId = classId });
            }

            resultId = entity.Id;
            auditAction = contentChanged ? "Korrigiert" : "Geändert";
        }

        await db.SaveChangesAsync(ct);

        var updated = await db.CurriculumItems.Include(x => x.Classes).FirstAsync(x => x.Id == resultId, ct);
        await auditWriter.WriteAsync(actor.UserId, actor.UserName, auditAction,
            "Ausbildungsplan-Punkt", title, oldSnapshot, await SnapshotAsync(updated, ct), ct);

        return await ReloadDtoAsync(resultId, ct);
    }

    public async Task DeleteAsync(Guid id, Actor actor, CancellationToken ct = default)
    {
        var entity = await db.CurriculumItems.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException("Dieser Punkt wurde nicht gefunden. Vielleicht wurde er bereits gelöscht.");

        // Soft-delete the current version; older versions remain untouched
        // (student checklists will reference them later).
        entity.IsDeleted = true;
        entity.DeletedAtUtc = DateTime.UtcNow;
        entity.DeletedByUserId = actor.UserId;
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(actor.UserId, actor.UserName, "Gelöscht",
            "Ausbildungsplan-Punkt", entity.Title, oldValuesJson: await SnapshotAsync(entity, ct), cancellationToken: ct);
    }

    private static void ThrowIfInvalid(string title, int? requiredCount)
    {
        var errors = CurriculumRules.Validate(title, requiredCount);
        if (errors.Count > 0)
        {
            throw new AppValidationException(string.Join(" ", errors));
        }
    }

    /// <summary>Do all referenced classes actually exist? (guards against broken links)</summary>
    private async Task EnsureClassesExistAsync(Guid[] classIds, CancellationToken ct)
    {
        var distinct = classIds.Distinct().ToArray();
        if (distinct.Length == 0) return;

        var found = await db.LicenseClasses.CountAsync(x => distinct.Contains(x.Id), ct);
        if (found != distinct.Length)
        {
            throw new AppValidationException(
                "Mindestens eine der gewählten Klassen existiert nicht mehr. Bitte die Seite neu laden.");
        }
    }

    private CurriculumItemDto ToDto(CurriculumItem x) => new()
    {
        Id = x.Id,
        ItemKey = x.ItemKey,
        Version = x.Version,
        ValidFromUtc = x.ValidFromUtc,
        Section = x.Section,
        Title = x.Title,
        RequiredCount = x.RequiredCount,
        IsActive = x.IsActive,
        SortOrder = x.SortOrder,
        ClassIds = [.. x.Classes.Select(c => c.LicenseClassId)],
        ClassCodes = [.. x.Classes
            .Where(c => c.LicenseClass != null)
            .Select(c => c.LicenseClass!.Code)
            .OrderBy(code => code)],
        RowVersion = db.Entry(x).Property<uint>("xmin").CurrentValue,
    };

    private async Task<CurriculumItemDto> ReloadDtoAsync(Guid id, CancellationToken ct)
    {
        var entity = await db.CurriculumItems
            .Include(x => x.Classes).ThenInclude(c => c.LicenseClass)
            .FirstAsync(x => x.Id == id, ct);
        return ToDto(entity);
    }

    /// <summary>State as JSON for the audit log (class codes instead of IDs - readable).</summary>
    private async Task<string> SnapshotAsync(CurriculumItem x, CancellationToken ct)
    {
        var classIds = x.Classes.Select(c => c.LicenseClassId).ToList();
        var codes = await db.LicenseClasses
            .Where(k => classIds.Contains(k.Id))
            .Select(k => k.Code).OrderBy(c => c).ToListAsync(ct);

        return JsonSerializer.Serialize(new
        {
            x.Section, x.Title, x.RequiredCount, x.Version, x.IsActive, x.SortOrder,
            Klassen = codes.Count == 0 ? "alle" : string.Join(", ", codes),
        });
    }
}
