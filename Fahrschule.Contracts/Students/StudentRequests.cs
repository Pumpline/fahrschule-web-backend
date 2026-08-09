using System.ComponentModel.DataAnnotations;

namespace Fahrschule.Contracts.Students;

/// <summary>"Create new student" request.</summary>
public class CreateStudentRequest
{
    [Required(ErrorMessage = "Bitte den Vornamen eintragen.")]
    public string FirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Bitte den Nachnamen eintragen.")]
    public string LastName { get; set; } = string.Empty;

    /// <summary>The record number ("Journalnummer"), optional.</summary>
    public string? JournalNumber { get; set; }

    public DateOnly? DateOfBirth { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? Notes { get; set; }
}

/// <summary>"Update student master data" request - with version marker.</summary>
public class UpdateStudentRequest
{
    [Required(ErrorMessage = "Bitte den Vornamen eintragen.")]
    public string FirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Bitte den Nachnamen eintragen.")]
    public string LastName { get; set; } = string.Empty;

    /// <summary>The record number ("Journalnummer"). Like the name it is always
    /// applied - it is never hidden, so it is never sent blank by accident.</summary>
    public string? JournalNumber { get; set; }

    public DateOnly? DateOfBirth { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? Notes { get; set; }

    /// <summary>Which sensitive fields the client actually loaded/edited and is
    /// therefore allowed to overwrite. Fields not listed are left unchanged - this
    /// is what makes the lazy "reveal only what you need" approach safe to save.
    /// (The name is always updated.)</summary>
    public List<string> EditableFields { get; set; } = [];

    public uint Version { get; set; }
}

/// <summary>
/// "Set the Vorbesitz" request - the licences the student ALREADY holds, plus
/// what that means for the Grundstoff. One request for the whole block, because
/// the file edits it in one dialog ("Führerschein eintragen") and applies it
/// with one button. It lives next to the training classes in the progress tab,
/// which is where it changes something - hence its own endpoint rather than a
/// ride-along on the master-data save.
/// </summary>
public class SetStudentPriorLicenseRequest
{
    /// <summary>The COMPLETE list of already-held classes; missing ones are removed.</summary>
    public List<Guid> LicenseClassIds { get; set; } = [];

    /// <summary>Free text for a licence outside the school's class list
    /// (e.g. a foreign one). Counts as Vorbesitz just like a picked class.</summary>
    public string? Note { get; set; }

    /// <summary>Fixed number of Grundstoff double lessons for this student.
    /// null (or 0) = let the program derive it from the Vorbesitz.</summary>
    public int? RequiredBasicTheoryLessonsOverride { get; set; }

    /// <summary>Why the fixed number was set (kept for the audit log).</summary>
    public string? RequiredBasicTheoryLessonsOverrideReason { get; set; }
}

/// <summary>"Add a licence class to a student" request.</summary>
public class AddStudentLicenseClassRequest
{
    [Required(ErrorMessage = "Bitte eine Führerscheinklasse wählen.")]
    public Guid LicenseClassId { get; set; }
}

/// <summary>"Change the phase of a student's licence class" request.</summary>
public class SetStudentPhaseRequest
{
    [Required(ErrorMessage = "Bitte eine Phase angeben.")]
    public string Phase { get; set; } = string.Empty;
}
