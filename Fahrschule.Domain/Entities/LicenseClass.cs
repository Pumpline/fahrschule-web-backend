using Fahrschule.Domain.Common;

namespace Fahrschule.Domain.Entities;

/// <summary>
/// A driving licence class (B, BE, A1, ...) - the first building block of the
/// configuration foundation (project rule 3: business content is DATA, not
/// code). The owner maintains classes in the admin panel; when laws change,
/// data is edited here instead of writing new code.
/// </summary>
public class LicenseClass : ISoftDeletable
{
    public Guid Id { get; set; }

    /// <summary>The official short code, e.g. "B", "A1", "BE". Unique.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Human-readable description, e.g. "Pkw bis 3,5 t".</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Minimum age in years (used later to validate student
    /// registrations). null = no minimum age stored.</summary>
    public int? MinimumAge { get; set; }

    /// <summary>Prerequisites as free text, e.g. "Vorbesitz Klasse B"
    /// or "BF17: begleitetes Fahren ab 17 möglich".</summary>
    public string? Requirements { get; set; }

    /// <summary>Inactive = no longer selectable for NEW registrations;
    /// students already training for this class keep their data.
    /// (Deactivating is deliberately different from deleting.)</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Position in lists (smaller number = higher up).</summary>
    public int SortOrder { get; set; }

    // --- Training-plan target numbers (KONZEPT 3.3, editable per class) ---
    // These drive the practice/extra-theory counters in the student's progress.
    // 0 = not applicable for this class. Legal defaults are seeded (e.g. class B),
    // the owner edits them in the admin panel when the law changes (project rule 3).

    /// <summary>Class-specific theory double lessons (Zusatzstoff), e.g. B = 2.</summary>
    public int RequiredTheoryDoubleLessons { get; set; }

    /// <summary>Mandatory special drives: overland trips (Überlandfahrt), e.g. B = 5.</summary>
    public int RequiredSpecialDrivesOverland { get; set; }

    /// <summary>Mandatory special drives: motorway trips (Autobahnfahrt), e.g. B = 4.</summary>
    public int RequiredSpecialDrivesHighway { get; set; }

    /// <summary>Mandatory special drives: night trips (Nachtfahrt), e.g. B = 3.</summary>
    public int RequiredSpecialDrivesNight { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    // Soft delete (project rule 7): deleting only flags the record;
    // actual removal is done later by the retention job.
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public Guid? DeletedByUserId { get; set; }
}
