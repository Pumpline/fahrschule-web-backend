using System.Text.Json;
using Fahrschule.Application.Audit;
using Fahrschule.Application.Common;
using Fahrschule.Application.LicenseClasses;
using Fahrschule.Contracts.Documents;
using Fahrschule.Domain.Entities;
using Fahrschule.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fahrschule.Application.Documents;

public interface IDocumentCatalogService
{
    Task<List<DocumentCatalogItemDto>> GetAllAsync(CancellationToken ct = default);
    Task<DocumentCatalogItemDto> CreateAsync(CreateDocumentCatalogItemRequest request, Actor actor, CancellationToken ct = default);
    Task<DocumentCatalogItemDto> UpdateAsync(Guid id, UpdateDocumentCatalogItemRequest request, Actor actor, CancellationToken ct = default);
    Task DeleteAsync(Guid id, Actor actor, CancellationToken ct = default);
}

/// <summary>
/// Business logic for maintaining the document catalogue (admin panel).
/// Same pattern as the other configuration data: audit log, soft delete,
/// optimistic concurrency, class M:N (empty = all classes).
/// </summary>
public class DocumentCatalogService(FahrschuleDbContext db, IAuditWriter auditWriter) : IDocumentCatalogService
{
    public async Task<List<DocumentCatalogItemDto>> GetAllAsync(CancellationToken ct = default)
    {
        var items = await db.DocumentCatalogItems
            .Include(x => x.Classes).ThenInclude(c => c.LicenseClass)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Name)
            .ToListAsync(ct);

        return items.Select(x => ToDto(x)).ToList();
    }

    public async Task<DocumentCatalogItemDto> CreateAsync(CreateDocumentCatalogItemRequest request, Actor actor, CancellationToken ct = default)
    {
        var name = DocumentCatalogRules.NormalizeName(request.Name);
        ThrowIfInvalid(name);
        await EnsureClassesExistAsync(request.ClassIds, ct);

        var now = DateTime.UtcNow;
        var entity = new DocumentCatalogItem
        {
            Id = Guid.NewGuid(),
            Name = name,
            Note = NullIfEmpty(request.Note),
            ExpiryDateRequired = request.ExpiryDateRequired,
            IsActive = request.IsActive,
            SortOrder = request.SortOrder,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Classes = [.. request.ClassIds.Distinct().Select(id => new DocumentCatalogItemClass { LicenseClassId = id })],
        };

        db.DocumentCatalogItems.Add(entity);
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(actor.UserId, actor.UserName, "Angelegt",
            "Unterlage", name, newValuesJson: await SnapshotAsync(entity, ct), cancellationToken: ct);

        return await ReloadDtoAsync(entity.Id, ct);
    }

    public async Task<DocumentCatalogItemDto> UpdateAsync(Guid id, UpdateDocumentCatalogItemRequest request, Actor actor, CancellationToken ct = default)
    {
        var entity = await db.DocumentCatalogItems
            .Include(x => x.Classes)
            .FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException("Diese Unterlage wurde nicht gefunden. Bitte die Liste neu laden.");

        var name = DocumentCatalogRules.NormalizeName(request.Name);
        ThrowIfInvalid(name);
        await EnsureClassesExistAsync(request.ClassIds, ct);

        var oldSnapshot = await SnapshotAsync(entity, ct);

        entity.Name = name;
        entity.Note = NullIfEmpty(request.Note);
        entity.ExpiryDateRequired = request.ExpiryDateRequired;
        entity.IsActive = request.IsActive;
        entity.SortOrder = request.SortOrder;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        // Replace the class assignment: remove old links, add the new ones.
        entity.Classes.Clear();
        foreach (var classId in request.ClassIds.Distinct())
        {
            entity.Classes.Add(new DocumentCatalogItemClass { LicenseClassId = classId });
        }

        // Optimistic concurrency (see LicenseClassService).
        db.Entry(entity).Property<uint>("xmin").OriginalValue = request.Version;

        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(actor.UserId, actor.UserName, "Geändert",
            "Unterlage", name, oldSnapshot, await SnapshotAsync(entity, ct), ct);

        return await ReloadDtoAsync(entity.Id, ct);
    }

    public async Task DeleteAsync(Guid id, Actor actor, CancellationToken ct = default)
    {
        var entity = await db.DocumentCatalogItems.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException("Diese Unterlage wurde nicht gefunden. Vielleicht wurde sie bereits gelöscht.");

        // Soft delete (project rule 7).
        entity.IsDeleted = true;
        entity.DeletedAtUtc = DateTime.UtcNow;
        entity.DeletedByUserId = actor.UserId;
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(actor.UserId, actor.UserName, "Gelöscht",
            "Unterlage", entity.Name, oldValuesJson: await SnapshotAsync(entity, ct), cancellationToken: ct);
    }

    private static void ThrowIfInvalid(string name)
    {
        var errors = DocumentCatalogRules.Validate(name);
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

    private DocumentCatalogItemDto ToDto(DocumentCatalogItem x) => new()
    {
        Id = x.Id,
        Name = x.Name,
        Note = x.Note,
        ExpiryDateRequired = x.ExpiryDateRequired,
        IsActive = x.IsActive,
        SortOrder = x.SortOrder,
        ClassIds = [.. x.Classes.Select(c => c.LicenseClassId)],
        ClassCodes = [.. x.Classes
            .Where(c => c.LicenseClass != null)
            .Select(c => c.LicenseClass!.Code)
            .OrderBy(code => code)],
        Version = db.Entry(x).Property<uint>("xmin").CurrentValue,
    };

    private async Task<DocumentCatalogItemDto> ReloadDtoAsync(Guid id, CancellationToken ct)
    {
        var entity = await db.DocumentCatalogItems
            .Include(x => x.Classes).ThenInclude(c => c.LicenseClass)
            .FirstAsync(x => x.Id == id, ct);
        return ToDto(entity);
    }

    /// <summary>State as JSON for the audit log (class codes instead of IDs - readable).</summary>
    private async Task<string> SnapshotAsync(DocumentCatalogItem x, CancellationToken ct)
    {
        var classIds = x.Classes.Select(c => c.LicenseClassId).ToList();
        var codes = await db.LicenseClasses
            .Where(k => classIds.Contains(k.Id))
            .Select(k => k.Code).OrderBy(c => c).ToListAsync(ct);

        return JsonSerializer.Serialize(new
        {
            x.Name, x.Note, x.ExpiryDateRequired, x.IsActive, x.SortOrder,
            Klassen = codes.Count == 0 ? "alle" : string.Join(", ", codes),
        });
    }

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
