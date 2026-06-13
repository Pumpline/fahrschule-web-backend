namespace Fahrschule.Contracts.Students;

/// <summary>One licence class of a student, with its phase.</summary>
public class StudentLicenseClassDto
{
    public Guid LicenseClassId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    /// <summary>Phase as the enum name (Theory / TheoryExam / Practice / PracticeExam / Completed).</summary>
    public string Phase { get; set; } = string.Empty;
}

/// <summary>
/// A row in the student list. Data minimisation: only the aggregated progress
/// is shown here, never the details (KONZEPT 3.1).
/// </summary>
public class StudentListItemDto
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    /// <summary>Codes of the active licence classes, e.g. ["B", "A1"].</summary>
    public string[] ClassCodes { get; set; } = [];
    /// <summary>Aggregated progress in percent (phase-based stand-in until step 4).</summary>
    public int ProgressPercent { get; set; }
}

/// <summary>A page of the student list.</summary>
public class StudentListResultDto
{
    public List<StudentListItemDto> Items { get; set; } = [];
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

/// <summary>Full student record (the "Akte"), shown on the detail page.</summary>
public class StudentDetailDto
{
    public Guid Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public DateOnly? DateOfBirth { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Street { get; set; }
    public string? PostalCode { get; set; }
    public string? City { get; set; }
    public string? Notes { get; set; }
    public List<StudentLicenseClassDto> Classes { get; set; } = [];

    /// <summary>Version marker against mutual overwrites (PostgreSQL xmin).</summary>
    public uint Version { get; set; }
}
