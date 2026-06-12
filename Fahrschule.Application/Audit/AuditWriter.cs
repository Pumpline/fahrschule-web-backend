using Fahrschule.Domain.Entities;
using Fahrschule.Infrastructure.Persistence;

namespace Fahrschule.Application.Audit;

/// <summary>Writes entries to the audit log ("who changed what, when").</summary>
public interface IAuditWriter
{
    /// <summary>Appends an entry to the audit log (append-only).
    /// Caution: never put passwords/secrets into old/new values!
    /// Note: action/entityType are passed in GERMAN - the owner reads the
    /// log in the admin panel.</summary>
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
