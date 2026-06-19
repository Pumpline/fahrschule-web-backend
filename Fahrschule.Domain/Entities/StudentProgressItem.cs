namespace Fahrschule.Domain.Entities;

/// <summary>
/// One point of a student's PERSONAL training checklist (KONZEPT 3.3 / 3.3a).
///
/// This is a SNAPSHOT: when a student registers for a licence class, the then
/// valid curriculum points are copied into their own checklist. We store the
/// title/section/required-count as they were AT THAT TIME, plus the version of
/// the curriculum item we copied. Later master changes (new laws) therefore do
/// NOT act retroactively - the training record always shows what applied when
/// the student trained (legally clean for the Ausbildungsnachweis).
///
/// Shared "Grundstoff" points (a curriculum item that applies to several of the
/// student's classes) are kept ONCE and counted for all of them - that is what
/// <see cref="Classes"/> records.
/// </summary>
public class StudentProgressItem
{
    public Guid Id { get; set; }

    public Guid StudentId { get; set; }
    public Student? Student { get; set; }

    // --- snapshot of the curriculum item (KONZEPT 3.3a) ---

    /// <summary>Fixed identity of the curriculum item (same across all versions).
    /// Lets us detect later whether the master point still matches this snapshot
    /// (needed for the "Anrechnung" step 4c).</summary>
    public Guid CurriculumItemKey { get; set; }

    /// <summary>Which version of the curriculum item was snapshotted.</summary>
    public int CurriculumItemVersion { get; set; }

    /// <summary>Section at snapshot time, e.g. "Theorie-Grundstoff" (German -
    /// shown to users).</summary>
    public string Section { get; set; } = string.Empty;

    /// <summary>Title at snapshot time, e.g. "Vorfahrt und Verkehrsregelungen".</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Target count for countable points (e.g. Überlandfahrt: 5).
    /// null = a simple check-off point (e.g. a theory topic).</summary>
    public int? RequiredCount { get; set; }

    /// <summary>Display order, copied from the curriculum item.</summary>
    public int SortOrder { get; set; }

    // --- status (filled in while the student trains) ---

    /// <summary>Done? For simple points this is a DERIVED, stored value:
    /// <c>ManuallyCompleted || covered by a non-deleted lesson</c> (kept in sync
    /// whenever a lesson or a manual mark changes). For countable points it is
    /// unused (done = reached the count) - see StudentProgressRules.</summary>
    public bool IsCompleted { get; set; }

    /// <summary>Simple point completed OUTSIDE a recorded lesson - the exception
    /// path: a manual tick (e.g. Anrechnung/Übernahme from another school) or a
    /// theory-attendance entry. Together with lesson coverage it drives
    /// <see cref="IsCompleted"/>, so removing a lesson can recompute completion
    /// correctly without wrongly un-ticking a manually credited point.</summary>
    public bool ManuallyCompleted { get; set; }

    /// <summary>"Erledigt am" - the date the point was completed (KONZEPT 3.3:
    /// the small date field that opens on first check-off).</summary>
    public DateOnly? CompletedOn { get; set; }

    /// <summary>Optional note ("nachgeholt", "Sondertermin", ...).</summary>
    public string? Note { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>The student's classes this point counts for. EMPTY = it applies
    /// to ALL of the student's classes (a shared point whose curriculum item had
    /// no class restriction).</summary>
    public List<StudentProgressItemClass> Classes { get; set; } = [];

    /// <summary>For countable points: one row per counted session (KONZEPT 3.3:
    /// "jede gezählte Stunde bekommt eine eigene Zeile mit Datum + Notiz").</summary>
    public List<StudentProgressEntry> Entries { get; set; } = [];
}

/// <summary>Link between a progress point and one of the student's licence
/// classes (snapshot of which classes the point counts for).</summary>
public class StudentProgressItemClass
{
    public Guid StudentProgressItemId { get; set; }
    public StudentProgressItem? StudentProgressItem { get; set; }

    public Guid LicenseClassId { get; set; }
    public LicenseClass? LicenseClass { get; set; }
}

/// <summary>
/// One counted session of a countable progress point (KONZEPT 3.3): a special
/// drive, an extra theory double lesson, etc. Each "+" adds a row with a date
/// and an optional note; "−" removes the last one.
/// </summary>
public class StudentProgressEntry
{
    public Guid Id { get; set; }

    public Guid StudentProgressItemId { get; set; }
    public StudentProgressItem? StudentProgressItem { get; set; }

    /// <summary>The lesson this counted session belongs to (the new model: every
    /// new counted session is backed by a recorded lesson). null = a legacy or
    /// manually credited session with no lesson. When the lesson is soft-deleted
    /// the session stops counting (query filter), but stays recoverable.</summary>
    public Guid? LessonId { get; set; }
    public Lesson? Lesson { get; set; }

    /// <summary>When this session took place.</summary>
    public DateOnly PerformedOn { get; set; }

    /// <summary>Optional note ("1. Fahrt", "Autobahn", ...).</summary>
    public string? Note { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
