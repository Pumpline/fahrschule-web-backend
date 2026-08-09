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

/// <summary>A licence class the student already holds ("Vorbesitz").</summary>
public class StudentPriorLicenseClassDto
{
    public Guid LicenseClassId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// What the student brings along and what that means for the Grundstoff
/// (§ 4 Abs. 3 FahrschAusbO). Bundled so the file and the PDF read the same
/// numbers instead of each deriving them again.
/// </summary>
public class StudentPriorLicenseDto
{
    /// <summary>Classes out of the school's list that the student already holds.</summary>
    public List<StudentPriorLicenseClassDto> Classes { get; set; } = [];

    /// <summary>Free text for prior licences outside that list (e.g. foreign).</summary>
    public string? Note { get; set; }

    /// <summary>True when anything is recorded - the condition the regulation
    /// actually asks about ("besitzt bereits eine Fahrerlaubnis").</summary>
    public bool HasPriorLicense { get; set; }

    /// <summary>Grundstoff double lessons required for this student, after the
    /// Vorbesitz and any override have been applied.</summary>
    public int RequiredBasicTheoryLessons { get; set; }

    /// <summary>Set when the instructor overrode the derived number.</summary>
    public int? RequiredBasicTheoryLessonsOverride { get; set; }

    public string? RequiredBasicTheoryLessonsOverrideReason { get; set; }
}

/// <summary>
/// A row in the student list. Data minimisation: only the aggregated progress
/// is shown here, never the details (KONZEPT 3.1).
/// </summary>
public class StudentListItemDto
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;

    /// <summary>The driving school's own record number ("Journalnummer"), so the
    /// list can be matched against the paper journal.</summary>
    public string? JournalNumber { get; set; }
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

/// <summary>
/// The full student record - INTERNAL use only (data export, PDF documents).
/// Never returned to the browser as a whole, so the sensitive fields are not
/// preloaded (see <see cref="StudentAkteDto"/> + the per-field reveal endpoint).
/// </summary>
public class StudentDetailDto
{
    public Guid Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? JournalNumber { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? Notes { get; set; }
    public List<StudentLicenseClassDto> Classes { get; set; } = [];

    /// <summary>Prior licences and the resulting Grundstoff requirement.</summary>
    public StudentPriorLicenseDto PriorLicense { get; set; } = new();

    /// <summary>Version marker against mutual overwrites (PostgreSQL xmin).</summary>
    public uint Version { get; set; }
}

/// <summary>
/// The lightweight student record shown on the detail page ("Akte"). For data
/// minimisation (GDPR) it carries NO sensitive values - only the name, the
/// classes, and which sensitive fields are filled vs empty. The actual values
/// are fetched one at a time via the reveal endpoint (which is audited).
/// </summary>
public class StudentAkteDto
{
    public Guid Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;

    /// <summary>The record number ("Journalnummer"). Like the name it is shown
    /// openly: it is an internal file number the office needs constantly, and it
    /// is printed on the documents anyway.</summary>
    public string? JournalNumber { get; set; }

    public List<StudentLicenseClassDto> Classes { get; set; } = [];

    /// <summary>Prior licences and the resulting Grundstoff requirement. Training
    /// data like the classes themselves, so it is shown openly (not behind the 👁).</summary>
    public StudentPriorLicenseDto PriorLicense { get; set; } = new();

    public uint Version { get; set; }

    /// <summary>The sensitive master-data fields, each marked filled or empty
    /// (so empty ones can be filled in without first revealing anything).</summary>
    public List<StudentFieldStatusDto> Fields { get; set; } = [];
}

/// <summary>One sensitive master-data field: its key, label and whether it holds a value.</summary>
public class StudentFieldStatusDto
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool HasValue { get; set; }
}

/// <summary>The revealed value of a single master-data field.</summary>
public class StudentFieldValueDto
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
