using Fahrschule.Domain.Entities;
using Fahrschule.Infrastructure.Persistence;

namespace Fahrschule.Application.Audit;

/// <summary>Schreibt Einträge ins Audit-Log ("wer hat wann was geändert").</summary>
public interface IAuditWriter
{
    /// <summary>Hängt einen Eintrag an das Audit-Log an (append-only).
    /// Achtung: niemals Passwörter/Geheimnisse in old/new aufnehmen!</summary>
    Task WriteAsync(
        Guid? userId, string userName, string action,
        string entityType, string entityId,
        string? oldValuesJson = null, string? newValuesJson = null,
        CancellationToken cancellationToken = default);
}

public class AuditWriter(FahrschuleDbContext db) : IAuditWriter
{
    public async Task WriteAsync(
        Guid? userId, string userName, string action,
        string entityType, string entityId,
        string? oldValuesJson = null, string? newValuesJson = null,
        CancellationToken cancellationToken = default)
    {
        db.AuditLogs.Add(new AuditLog
        {
            TimestampUtc = DateTime.UtcNow,
            UserId = userId,
            UserName = userName,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            OldValuesJson = oldValuesJson,
            NewValuesJson = newValuesJson,
        });

        await db.SaveChangesAsync(cancellationToken);
    }
}
