using System.ComponentModel.DataAnnotations;

namespace Fahrschule.Contracts.Students;

/// <summary>"Create new student" request.</summary>
public class CreateStudentRequest
{
    [Required(ErrorMessage = "Bitte den Vornamen eintragen.")]
    public string FirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Bitte den Nachnamen eintragen.")]
    public string LastName { get; set; } = string.Empty;

    public DateOnly? DateOfBirth { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Street { get; set; }
    public string? PostalCode { get; set; }
    public string? City { get; set; }
    public string? Notes { get; set; }
}

/// <summary>"Update student master data" request - with version marker.</summary>
public class UpdateStudentRequest
{
    [Required(ErrorMessage = "Bitte den Vornamen eintragen.")]
    public string FirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Bitte den Nachnamen eintragen.")]
    public string LastName { get; set; } = string.Empty;

    public DateOnly? DateOfBirth { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Street { get; set; }
    public string? PostalCode { get; set; }
    public string? City { get; set; }
    public string? Notes { get; set; }

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
