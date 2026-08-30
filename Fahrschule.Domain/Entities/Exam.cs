namespace Fahrschule.Domain.Entities;

/// <summary>Theory or practical exam (KONZEPT 3.4).</summary>
public enum ExamKind
{
    Theory = 0,
    Practice = 1,
}

/// <summary>Result of an exam.</summary>
public enum ExamResult
{
    /// <summary>Planned / scheduled, no result yet.</summary>
    Planned = 0,
    Passed = 1,
    Failed = 2,
}

/// <summary>
/// An exam of a student for one licence class (KONZEPT 3.4). Real exams
/// (<see cref="IsPreliminary"/> = false) are counted as attempts and a failed
/// one starts a repeat lock. Preliminary exams ("Vorprüfung": internal practice
/// at the laptop / a test drive) are only NOTED - they never count as an attempt
/// and never trigger a lock.
///
/// The attempt number and the repeat lock are NOT stored: they are derived from
/// the exams and the logged lessons (the single source for hours), so editing
/// stays consistent (see ExamService / ExamRules).
/// </summary>
public class Exam
{
    public Guid Id { get; set; }

    public Guid StudentId { get; set; }
    public Student? Student { get; set; }

    public Guid LicenseClassId { get; set; }
    public LicenseClass? LicenseClass { get; set; }

    public ExamKind Kind { get; set; }

    /// <summary>true = "Vorprüfung" (only noted, no attempt, no lock).</summary>
    public bool IsPreliminary { get; set; }

    public DateOnly DateOn { get; set; }

    public ExamResult Result { get; set; }

    /// <summary>Optional note.</summary>
    public string? Note { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    // Soft-delete (project rule 7): deleting an exam only flags it, so it
    // vanishes from the list, the attempt count and the locks, but stays
    // recoverable and is purged only by the retention job.
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public Guid? DeletedByUserId { get; set; }
}
