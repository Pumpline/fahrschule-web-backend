using Fahrschule.Contracts.Admin;

namespace Fahrschule.Contracts.Dashboard;

/// <summary>The start-page dashboard (KONZEPT 3.1/3a, mockup screen 1).</summary>
public class DashboardDto
{
    /// <summary>Documents expiring within the reminder window (or already
    /// overdue) - "Bald fällig", so the office can inform the student.</summary>
    public List<UpcomingDocumentDto> UpcomingDocuments { get; set; } = [];

    /// <summary>The most recent audit-log entries ("Letzte Änderungen").</summary>
    public List<AuditLogDto> RecentChanges { get; set; } = [];
}

/// <summary>One soon-expiring document of a student.</summary>
public class UpcomingDocumentDto
{
    public Guid StudentId { get; set; }
    public string StudentName { get; set; } = string.Empty;
    public string DocumentName { get; set; } = string.Empty;
    public DateOnly ExpiresOn { get; set; }

    /// <summary>Days until expiry (negative = already overdue).</summary>
    public int? DaysUntilExpiry { get; set; }
}
