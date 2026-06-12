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
    /// <summary>Alle AKTUELL gültigen Punkte (neueste Version je Kennung).</summary>
    Task<List<CurriculumItemDto>> GetCurrentAsync(string? section = null, CancellationToken ct = default);
    Task<CurriculumItemDto> CreateAsync(CreateCurriculumItemRequest request, Actor actor, CancellationToken ct = default);
    Task<CurriculumItemDto> UpdateAsync(Guid id, UpdateCurriculumItemRequest request, Actor actor, CancellationToken ct = default);
    Task DeleteAsync(Guid id, Actor actor, CancellationToken ct = default);
}

/// <summary>
/// Fachlogik für die Pflege der Ausbildungsplan-Punkte (Adminpanel).
///
/// Kern ist die VERSIONIERUNG (KONZEPT 3.3a): Inhaltliche Änderungen legen
/// eine neue Zeile mit Version+1 an und markieren die alte als abgelöst –
/// gelöscht wird nie. Schüler-Checklisten (kommen in Schritt 4) verweisen
/// dann auf die Version, die zu ihrer Anmeldung galt.
/// </summary>
public class CurriculumItemService(FahrschuleDbContext db, IAuditWriter auditWriter) : ICurriculumItemService
{
    public async Task<List<CurriculumItemDto>> GetCurrentAsync(string? section = null, CancellationToken ct = default)
    {
        var query = db.CurriculumItems
            .Where(x => x.SupersededAtUtc == null); // nur aktuelle Versionen

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
        ThrowWennUngueltig(title, request.RequiredCount);
        await PruefeKlassenAsync(request.ClassIds, ct);

        var now = DateTime.UtcNow;
        var entity = new CurriculumItem
        {
            Id = Guid.NewGuid(),
            ItemKey = Guid.NewGuid(), // neue feste Kennung – bleibt über alle Versionen
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
            // Jemand hat parallel schon eine neuere Version erzeugt.
            throw new AppValidationException(
                "Von diesem Punkt gibt es inzwischen eine neuere Version. Bitte die Liste neu laden und dort weiterarbeiten.");
        }

        var title = CurriculumRules.NormalizeTitle(request.Title);
        ThrowWennUngueltig(title, request.RequiredCount);
        await PruefeKlassenAsync(request.ClassIds, ct);

        var oldSnapshot = await SnapshotAsync(entity, ct);
        var oldClassIds = entity.Classes.Select(c => c.LicenseClassId).ToList();
        var now = DateTime.UtcNow;

        // Versionsmarke des Bearbeiters anlegen – schützt vor gegenseitigem Überschreiben.
        db.Entry(entity).Property<uint>("xmin").OriginalValue = request.RowVersion;

        Guid resultId;
        string aktion;

        if (CurriculumRules.NeedsNewVersion(
                entity.Title, title, entity.RequiredCount, request.RequiredCount, oldClassIds, request.ClassIds))
        {
            // Inhalt geändert → alte Version ablösen, neue Zeile anlegen.
            entity.SupersededAtUtc = now;

            var neueVersion = new CurriculumItem
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
            db.CurriculumItems.Add(neueVersion);
            resultId = neueVersion.Id;
            aktion = $"Geändert (neue Version {neueVersion.Version})";
        }
        else
        {
            // Nur organisatorisch (aktiv/Reihenfolge) → gleiche Version anpassen.
            entity.IsActive = request.IsActive;
            entity.SortOrder = request.SortOrder;
            entity.UpdatedAtUtc = now;
            resultId = entity.Id;
            aktion = "Geändert";
        }

        await db.SaveChangesAsync(ct);

        var neu = await db.CurriculumItems.Include(x => x.Classes).FirstAsync(x => x.Id == resultId, ct);
        await auditWriter.WriteAsync(actor.UserId, actor.UserName, aktion,
            "Ausbildungsplan-Punkt", title, oldSnapshot, await SnapshotAsync(neu, ct), ct);

        return await ReloadDtoAsync(resultId, ct);
    }

    public async Task DeleteAsync(Guid id, Actor actor, CancellationToken ct = default)
    {
        var entity = await db.CurriculumItems.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException("Dieser Punkt wurde nicht gefunden. Vielleicht wurde er bereits gelöscht.");

        // Soft-Delete der aktuellen Version; ältere Versionen bleiben unangetastet
        // (auf sie verweisen später die Schüler-Checklisten).
        entity.IsDeleted = true;
        entity.DeletedAtUtc = DateTime.UtcNow;
        entity.DeletedByUserId = actor.UserId;
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(actor.UserId, actor.UserName, "Gelöscht",
            "Ausbildungsplan-Punkt", entity.Title, oldValuesJson: await SnapshotAsync(entity, ct), cancellationToken: ct);
    }

    private static void ThrowWennUngueltig(string title, int? requiredCount)
    {
        var errors = CurriculumRules.Validate(title, requiredCount);
        if (errors.Count > 0)
        {
            throw new AppValidationException(string.Join(" ", errors));
        }
    }

    /// <summary>Existieren alle angegebenen Klassen wirklich? (Schutz vor kaputten Verweisen)</summary>
    private async Task PruefeKlassenAsync(Guid[] classIds, CancellationToken ct)
    {
        var distinct = classIds.Distinct().ToArray();
        if (distinct.Length == 0) return;

        var vorhanden = await db.LicenseClasses.CountAsync(x => distinct.Contains(x.Id), ct);
        if (vorhanden != distinct.Length)
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

    /// <summary>Stand als JSON fürs Audit-Log (mit Klassen-Kürzeln statt IDs – lesbar).</summary>
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
