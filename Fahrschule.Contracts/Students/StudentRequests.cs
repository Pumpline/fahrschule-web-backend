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
