namespace Fahrschule.Contracts.Admin;

/// <summary>
/// The retention picture shown in the admin panel (KONZEPT 3.7 / rule 7): the
/// configured retention period plus every student currently marked for
/// deletion, each with the date it will be removed permanently.
/// </summary>
public class RetentionStatusDto
{
    /// <summary>The configured retention period in days (editable in settings).</summary>
    public int RetentionDays { get; set; }

    /// <summary>How many of the marked students are already past their deadline.</summary>
    public int DueCount { get; set; }

    /// <summary>All students marked for deletion (newest first).</summary>
    public List<RetentionMarkedStudentDto> Marked { get; set; } = [];
}

/// <summary>A student marked for deletion, enriched with the deletion deadline.</summary>
public class RetentionMarkedStudentDto
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public List<string> ClassCodes { get; set; } = [];

    /// <summary>When the student was marked for deletion.</summary>
    public DateTime? DeletedAtUtc { get; set; }

    /// <summary>When the retention job will remove the data permanently
    /// (DeletedAtUtc + retention period).</summary>
    public DateTime? DueAtUtc { get; set; }

    /// <summary>True once the deadline has passed (the next job run removes it).</summary>
    public bool IsDue { get; set; }
}

/// <summary>The outcome of a retention run (manual or automatic).</summary>
public class RetentionRunResultDto
{
    /// <summary>How many students were removed permanently in this run.</summary>
    public int DeletedCount { get; set; }
}
