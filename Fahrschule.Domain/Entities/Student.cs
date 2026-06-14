using Fahrschule.Domain.Common;

namespace Fahrschule.Domain.Entities;

/// <summary>
/// A driving student (KONZEPT 3.1). The first big domain module.
///
/// Data minimisation (GDPR, project rule 1): we only store what the training
/// contract actually needs - name, date of birth, contact and address. NO
/// special categories (health, eyesight test result, ...) ever live here.
/// </summary>
public class Student : ISoftDeletable
{
    public Guid Id { get; set; }

    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;

    /// <summary>Date of birth - needed to check the minimum age per licence class.</summary>
    public DateOnly? DateOfBirth { get; set; }

    public string? Email { get; set; }
    public string? Phone { get; set; }

    /// <summary>Full address as one free-text field (Straße, PLZ, Ort combined).</summary>
    public string? Address { get; set; }

    /// <summary>Free-text notes (no special categories!).</summary>
    public string? Notes { get; set; }

    /// <summary>The licence classes this student is training for, each with its
    /// own phase (KONZEPT: status per class, not per student).</summary>
    public List<StudentLicenseClass> LicenseClasses { get; set; } = [];

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    // Soft delete (project rule 7): students are personal data with retention
    // rules, so deleting only flags; the retention job removes them later.
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public Guid? DeletedByUserId { get; set; }
}

/// <summary>
/// A student's registration for one licence class, carrying the phase
/// (KONZEPT 3.1 / data model: StudentLicenseClass holds the status per class).
/// </summary>
public class StudentLicenseClass
{
    public Guid StudentId { get; set; }
    public Student? Student { get; set; }

    public Guid LicenseClassId { get; set; }
    public LicenseClass? LicenseClass { get; set; }

    public StudentPhase Phase { get; set; } = StudentPhase.Theory;

    public DateTime AddedAtUtc { get; set; }
}
