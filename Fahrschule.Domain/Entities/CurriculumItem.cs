using Fahrschule.Domain.Common;

namespace Fahrschule.Domain.Entities;

/// <summary>
/// One item of the training curriculum - e.g. a theory topic, later also
/// basic driving exercises and special drives (KONZEPT 3.2/4).
///
/// Versioning (KONZEPT 3.3a): Every item has a FIXED identifier
/// (<see cref="ItemKey"/>) that stays the same across all versions. When the
/// content changes, a NEW row with version+1 is created; the old row is
/// flagged as superseded (<see cref="SupersededAtUtc"/>) and kept. This way
/// a student's training record always shows exactly the state that applied
/// at THEIR time - changes in the law never act retroactively.
/// </summary>
public class CurriculumItem : ISoftDeletable
{
    public Guid Id { get; set; }

    /// <summary>Fixed identifier across all versions (same item = same key).</summary>
    public Guid ItemKey { get; set; }

    /// <summary>Running version number (1, 2, 3 ...) per ItemKey.</summary>
    public int Version { get; set; }

    /// <summary>Since when this version applies (= time of the change).</summary>
    public DateTime ValidFromUtc { get; set; }

    /// <summary>Set as soon as a newer version exists.
    /// null = this is the currently valid version.</summary>
    public DateTime? SupersededAtUtc { get; set; }

    /// <summary>Curriculum section, e.g. "Theorie-Grundstoff",
    /// "Theorie-Zusatzstoff", later "Grundfahraufgaben", "Sonderfahrten".
    /// Deliberately text instead of a fixed enum - sections are data, not
    /// code (project rule 3). German values: the owner sees them in the UI.</summary>
    public string Section { get; set; } = string.Empty;

    /// <summary>Item title, e.g. "Vorfahrt und Verkehrsregelungen" (German -
    /// shown to users).</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Target count for countable items (e.g. Überlandfahrt: 5).
    /// null = simple check-off item (e.g. a theory topic).</summary>
    public int? RequiredCount { get; set; }

    /// <summary>Disabled items no longer apply to NEW registrations
    /// ("class X no longer needs reverse parking" → simply switch it off).</summary>
    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }

    /// <summary>Which classes does this item apply to? EMPTY = applies to ALL
    /// classes (typical for the shared basic theory material); otherwise only
    /// the assigned ones (KONZEPT 3.2: no monolithic "Grundstoff" block -
    /// assignment per item).</summary>
    public List<CurriculumItemClass> Classes { get; set; } = [];

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    // Soft delete (project rule 7)
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public Guid? DeletedByUserId { get; set; }
}

/// <summary>Link between curriculum item and licence class (M:N join table).</summary>
public class CurriculumItemClass
{
    public Guid CurriculumItemId { get; set; }
    public CurriculumItem? CurriculumItem { get; set; }

    public Guid LicenseClassId { get; set; }
    public LicenseClass? LicenseClass { get; set; }
}
