using System.Text.Json;
using Fahrschule.Application.Audit;
using Fahrschule.Application.Common;
using Fahrschule.Contracts.LicenseClasses;
using Fahrschule.Domain.Entities;
using Fahrschule.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fahrschule.Application.LicenseClasses;

/// <summary>Wer eine Änderung macht – für das Audit-Log (kommt aus dem Token).</summary>
public record Actor(Guid UserId, string UserName);

public interface ILicenseClassService
{
    Task<List<LicenseClassDto>> GetAllAsync(CancellationToken ct = default);
    Task<LicenseClassDto> CreateAsync(CreateLicenseClassRequest request, Actor actor, CancellationToken ct = default);
    Task<LicenseClassDto> UpdateAsync(Guid id, UpdateLicenseClassRequest request, Actor actor, CancellationToken ct = default);
    Task DeleteAsync(Guid id, Actor actor, CancellationToken ct = default);
}

/// <summary>
/// Fachlogik für die Pflege der Führerscheinklassen (Adminpanel).
///
/// Jede Änderung landet im Audit-Log (wer/wann/vorher/nachher – DSGVO-
/// Grundsatz, hier zwar keine personenbezogenen Daten, aber einheitliche
/// Nachvollziehbarkeit aller Stammdaten-Änderungen). Gelöscht wird nur
/// "weich" (Soft-Delete, Projektregel 7).
/// </summary>
public class LicenseClassService(FahrschuleDbContext db, IAuditWriter auditWriter) : ILicenseClassService
{
    public async Task<List<LicenseClassDto>> GetAllAsync(CancellationToken ct = default)
    {
        // Gelöschte Klassen filtert der globale Query-Filter automatisch heraus.
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
                // xmin = PostgreSQL-Systemspalte als Versionsmarke (siehe DTO).
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
            Requirements = NullWennLeer(request.Requirements),
            IsActive = request.IsActive,
            SortOrder = request.SortOrder,
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
        entity.Requirements = NullWennLeer(request.Requirements);
        entity.IsActive = request.IsActive;
        entity.SortOrder = request.SortOrder;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        // Optimistische Nebenläufigkeit: Wir sagen EF, von welcher Version der
        // Bearbeiter ausging. Stimmt sie nicht mehr mit der Datenbank überein,
        // wirft SaveChanges einen Konflikt (→ 409 mit verständlicher Meldung,
        // siehe ExceptionHandlingMiddleware) statt fremde Änderungen zu überschreiben.
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

        // Soft-Delete: nur markieren – wiederherstellbar, endgültiges Entfernen
        // übernimmt später der Aufbewahrungs-Job (Projektregel 7).
        entity.IsDeleted = true;
        entity.DeletedAtUtc = DateTime.UtcNow;
        entity.DeletedByUserId = actor.UserId;
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(actor.UserId, actor.UserName, "Gelöscht",
            "Führerscheinklasse", entity.Code, oldValuesJson: Snapshot(entity), cancellationToken: ct);
    }

    /// <summary>Form-Regeln + Eindeutigkeit des Kürzels prüfen.</summary>
    private async Task ValidateAsync(string code, int? minimumAge, Guid? existingId, CancellationToken ct)
    {
        var errors = LicenseClassRules.Validate(code, minimumAge);
        if (errors.Count > 0)
        {
            throw new AppValidationException(string.Join(" ", errors));
        }

        var doppelt = await db.LicenseClasses
            .AnyAsync(x => x.Code == code && (existingId == null || x.Id != existingId), ct);
        if (doppelt)
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
        // Nach SaveChanges liefert PostgreSQL die neue Versionsmarke zurück.
        Version = db.Entry(entity).Property<uint>("xmin").CurrentValue,
    };

    /// <summary>Stand der Klasse als JSON fürs Audit-Log (vorher/nachher).</summary>
    private static string Snapshot(LicenseClass x) => JsonSerializer.Serialize(new
    {
        x.Code, x.Description, x.MinimumAge, x.Requirements, x.IsActive, x.SortOrder,
    });

    private static string? NullWennLeer(string? wert)
        => string.IsNullOrWhiteSpace(wert) ? null : wert.Trim();
}
