namespace Fahrschule.Contracts.Admin;

/// <summary>One audit-log entry for the admin view (KONZEPT 3.7).</summary>
public class AuditLogDto
{
    public long Id { get; set; }
    public DateTime TimestampUtc { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string? OldValuesJson { get; set; }
    public string? NewValuesJson { get; set; }
}

/// <summary>A page of audit-log entries.</summary>
public class AuditListResultDto
{
    public List<AuditLogDto> Items { get; set; } = [];
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

/// <summary>A student marked for deletion ("Zur Löschung vorgemerkt", KONZEPT 3.7).</summary>
public class DeletedStudentDto
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public DateTime? DeletedAtUtc { get; set; }
    public List<string> ClassCodes { get; set; } = [];
}
