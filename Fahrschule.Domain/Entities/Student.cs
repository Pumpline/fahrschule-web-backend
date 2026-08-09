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

    /// <summary>
    /// The driving school's own record number for this student ("Journalnummer" /
    /// Schülerverzeichnis-Nr.). It links the digital file to the paper journal and
    /// is printed on the documents, so it is NOT hidden behind the reveal button:
    /// the office needs it at a glance. Free text (numbers, letters, "2026/014").
    /// </summary>
    public string? JournalNumber { get; set; }

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

    // --- Vorbesitz: licences the student ALREADY holds (§ 4 Abs. 3 FahrschAusbO) ---
    // "Besitzt der Fahrschüler bereits eine Fahrerlaubnis, so beträgt der Umfang
    // [des Grundstoffs] mindestens sechs Doppelstunden" (instead of twelve). Note
    // what the wording does NOT say: it does not matter WHICH class is held or
    // which is applied for - it is a plain yes/no. So this is a documented list
    // plus a derived flag, not a rule matrix.

    /// <summary>Licence classes the student already holds, out of the classes the
    /// school maintains. Also printed as "Vorbesitz Klasse(n)" on the record.</summary>
    public List<StudentPriorLicenseClass> PriorLicenseClasses { get; set; } = [];

    /// <summary>Free text for prior licences that are not in the school's class
    /// list (e.g. a foreign licence). Counts as Vorbesitz just like a picked class.</summary>
    public string? PriorLicenseNote { get; set; }

    /// <summary>
    /// Overrides the required number of Grundstoff double lessons for THIS student
    /// (null = derive it from the Vorbesitz and the settings). The escape hatch for
    /// the cases the regulation leaves open - a Mofa-Prüfbescheinigung is no
    /// Fahrerlaubnis, and § 4 says nothing about foreign licences - so the
    /// instructor decides instead of the code guessing.
    /// </summary>
    public int? RequiredBasicTheoryLessonsOverride { get; set; }

    /// <summary>Why the override was set (shown in the file, kept for the audit).</summary>
    public string? RequiredBasicTheoryLessonsOverrideReason { get; set; }

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

/// <summary>
/// A licence class the student ALREADY holds ("Vorbesitz"). Deliberately its own
/// link table rather than a flag on <see cref="StudentLicenseClass"/>: a prior
/// licence is not part of the training - it has no phase and no progress, it only
/// documents what the student brings along and shortens the Grundstoff
/// (§ 4 Abs. 3 FahrschAusbO).
/// </summary>
public class StudentPriorLicenseClass
{
    public Guid StudentId { get; set; }
    public Student? Student { get; set; }

    public Guid LicenseClassId { get; set; }
    public LicenseClass? LicenseClass { get; set; }

    public DateTime AddedAtUtc { get; set; }
}
