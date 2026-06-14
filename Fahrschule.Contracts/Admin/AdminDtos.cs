namespace Fahrschule.Contracts.Admin;

/// <summary>One audit-log entry for the admin view (KONZEPT 3.7).</summary>
public class AuditLogDto
{
    public long Id { get; set; }
    public DateTime TimestampUtc { get; set; }
    /// <summary>The initiator's CURRENT display name (resolved at read time), so
    /// a later rename is reflected; falls back to the stored name for deleted
    /// users or system actions.</summary>
    public string UserName { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string? OldValuesJson { get; set; }
    public string? NewValuesJson { get; set; }
    /// <summary>Topic key (e.g. "students") + German label, for grouping/filtering.</summary>
    public string Category { get; set; } = string.Empty;
    public string CategoryLabel { get; set; } = string.Empty;
    /// <summary>When the entry concerns a student, the student's id + current
    /// name so the UI can show the name with a link to the file. Null otherwise.</summary>
    public Guid? StudentId { get; set; }
    public string? StudentName { get; set; }
}

/// <summary>A page of audit-log entries, plus the categories this role may see.</summary>
public class AuditListResultDto
{
    public List<AuditLogDto> Items { get; set; } = [];
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    /// <summary>The categories visible to the current user (for the filter chips).</summary>
    public List<AuditCategoryDto> Categories { get; set; } = [];
}

/// <summary>One audit category: a stable key and its German label.</summary>
public class AuditCategoryDto
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
}

/// <summary>The role→category visibility configuration for the admin panel.
/// Admin always sees everything and is therefore not listed as editable.</summary>
public class AuditVisibilityDto
{
    /// <summary>All categories (key + label), in display order.</summary>
    public List<AuditCategoryDto> Categories { get; set; } = [];
    /// <summary>The editable roles (Fahrlehrer, Verwaltung) with their visible keys.</summary>
    public List<AuditRoleVisibilityDto> Roles { get; set; } = [];
}

/// <summary>Which category keys one role may see in the change log.</summary>
public class AuditRoleVisibilityDto
{
    public string Role { get; set; } = string.Empty;
    public List<string> Categories { get; set; } = [];
}

/// <summary>A student marked for deletion ("Zur Löschung vorgemerkt", KONZEPT 3.7).</summary>
public class DeletedStudentDto
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public DateTime? DeletedAtUtc { get; set; }
    public List<string> ClassCodes { get; set; } = [];
}
