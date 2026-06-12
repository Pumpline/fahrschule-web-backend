namespace Fahrschule.Domain.Common;

/// <summary>
/// Marks entities that are deleted "softly" (soft delete).
///
/// Why: Deleting must never remove data immediately (project rule + GDPR /
/// German tax law AO §147). Instead, the record is only flagged as deleted.
/// Actual removal is done later by a retention job once the legal retention
/// period has expired. Until then an admin can undo the deletion.
/// </summary>
public interface ISoftDeletable
{
    /// <summary>Has the record been flagged as deleted?</summary>
    bool IsDeleted { get; set; }

    /// <summary>When the record was flagged as deleted (UTC).</summary>
    DateTime? DeletedAtUtc { get; set; }

    /// <summary>Who deleted it? (for the audit log)</summary>
    Guid? DeletedByUserId { get; set; }
}
