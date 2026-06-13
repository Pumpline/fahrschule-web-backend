using Fahrschule.Application.Audit;
using Fahrschule.Application.Common;
using Fahrschule.Application.LicenseClasses;
using Fahrschule.Application.Settings;
using Fahrschule.Contracts.Students;
using Fahrschule.Domain.Entities;
using Fahrschule.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fahrschule.Application.Students;

public interface IStudentDocumentService
{
    Task<List<StudentDocumentDto>> GetForStudentAsync(Guid studentId, CancellationToken ct = default);
    Task<List<StudentDocumentDto>> SetStatusAsync(Guid studentId, Guid catalogItemId, SetStudentDocumentRequest request, Actor actor, CancellationToken ct = default);
}

/// <summary>
/// The per-student document checklist (KONZEPT 3.1). The list of REQUIRED
/// documents is derived: a catalogue item applies when it is active and either
/// applies to all classes or to one of the student's licence classes. The
/// stored statuses are merged on top. "Expiring soon" uses the configurable
/// reminder window from the settings.
/// </summary>
public class StudentDocumentService(
    FahrschuleDbContext db,
    ISettingsService settingsService,
    IAuditWriter auditWriter) : IStudentDocumentService
{
    public async Task<List<StudentDocumentDto>> GetForStudentAsync(Guid studentId, CancellationToken ct = default)
    {
        var (applicable, statuses) = await LoadApplicableAsync(studentId, ct);
        var reminderDays = (await settingsService.GetAsync(ct)).DocumentExpiryReminderDays;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        return applicable
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => ToDto(c, statuses.GetValueOrDefault(c.Id), today, reminderDays))
            .ToList();
    }

    public async Task<List<StudentDocumentDto>> SetStatusAsync(
        Guid studentId, Guid catalogItemId, SetStudentDocumentRequest request, Actor actor, CancellationToken ct = default)
    {
        var (applicable, statuses) = await LoadApplicableAsync(studentId, ct);

        var catalogItem = applicable.FirstOrDefault(c => c.Id == catalogItemId)
            ?? throw new NotFoundException("Diese Unterlage gehört nicht (mehr) zu den Klassen des Schülers. Bitte die Seite neu laden.");

        // Enforce the "expiry date required" rule (KONZEPT 3.1).
        var ruleError = DocumentChecklistRules.CheckCanBePresent(
            request.IsPresent, catalogItem.ExpiryDateRequired, request.ExpiresOn);
        if (ruleError is not null)
        {
            throw new AppValidationException(ruleError);
        }

        var existing = statuses.GetValueOrDefault(catalogItemId);
        if (existing is null)
        {
            existing = new DocumentChecklistItem { StudentId = studentId, DocumentCatalogItemId = catalogItemId };
            db.Set<DocumentChecklistItem>().Add(existing);
        }

        existing.IsPresent = request.IsPresent;
        existing.PresentedOn = request.PresentedOn;
        existing.ExpiresOn = request.ExpiresOn;
        existing.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(actor.UserId, actor.UserName, "Geändert",
            "Unterlage-Schüler", $"{studentId}/{catalogItem.Name}",
            newValuesJson: $"{{\"liegtVor\":{request.IsPresent.ToString().ToLowerInvariant()}}}", cancellationToken: ct);

        return await GetForStudentAsync(studentId, ct);
    }

    /// <summary>Loads the catalogue items that apply to the student plus the
    /// student's stored statuses (keyed by catalogue item id).</summary>
    private async Task<(List<DocumentCatalogItem> Applicable, Dictionary<Guid, DocumentChecklistItem> Statuses)>
        LoadApplicableAsync(Guid studentId, CancellationToken ct)
    {
        var student = await db.Students
            .Include(s => s.LicenseClasses)
            .FirstOrDefaultAsync(s => s.Id == studentId, ct)
            ?? throw new NotFoundException("Dieser Schüler wurde nicht gefunden. Bitte die Liste neu laden.");

        var studentClassIds = student.LicenseClasses.Select(lc => lc.LicenseClassId).ToHashSet();

        var catalog = await db.DocumentCatalogItems
            .Where(c => c.IsActive)
            .Include(c => c.Classes)
            .ToListAsync(ct);

        // Applies when it has no class assignment (all classes) or intersects
        // the student's classes.
        var applicable = catalog
            .Where(c => c.Classes.Count == 0 || c.Classes.Any(cc => studentClassIds.Contains(cc.LicenseClassId)))
            .ToList();

        var statuses = await db.Set<DocumentChecklistItem>()
            .Where(d => d.StudentId == studentId)
            .ToDictionaryAsync(d => d.DocumentCatalogItemId, ct);

        return (applicable, statuses);
    }

    private static StudentDocumentDto ToDto(DocumentCatalogItem catalog, DocumentChecklistItem? status, DateOnly today, int reminderDays)
        => new()
        {
            CatalogItemId = catalog.Id,
            Name = catalog.Name,
            Note = catalog.Note,
            ExpiryDateRequired = catalog.ExpiryDateRequired,
            IsPresent = status?.IsPresent ?? false,
            PresentedOn = status?.PresentedOn,
            ExpiresOn = status?.ExpiresOn,
            ExpiresSoon = DocumentChecklistRules.IsExpiringSoon(status?.ExpiresOn, today, reminderDays),
            DaysUntilExpiry = DocumentChecklistRules.DaysUntilExpiry(status?.ExpiresOn, today),
        };
}
