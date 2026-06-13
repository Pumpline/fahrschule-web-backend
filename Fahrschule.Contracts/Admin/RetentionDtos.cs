namespace Fahrschule.Contracts.Admin;

/// <summary>
/// The retention picture shown in the admin panel (KONZEPT 3.7 / § 31 FahrlG):
/// the configured retention period in years plus the students whose legal
/// deletion deadline has been reached and who will be removed at the next run.
/// </summary>
public class RetentionStatusDto
{
    /// <summary>Retention period in years after the end of the training year
    /// (editable in settings).</summary>
    public int RetentionYears { get; set; }

    /// <summary>How many students are currently due for permanent deletion.</summary>
    public int DueCount { get; set; }

    /// <summary>The students whose deadline has passed (soonest deadline first).</summary>
    public List<RetentionDueStudentDto> Due { get; set; } = [];
}

/// <summary>A student whose legal retention deadline has been reached.</summary>
public class RetentionDueStudentDto
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public List<string> ClassCodes { get; set; } = [];

    /// <summary>The day the training counted as finished (last lesson/exam, or
    /// registration date as fallback).</summary>
    public DateOnly TrainingEndDate { get; set; }

    /// <summary>The day the data became deletable (end of training year + frist).</summary>
    public DateOnly DueDate { get; set; }
}

/// <summary>The outcome of a retention run (manual or automatic).</summary>
public class RetentionRunResultDto
{
    /// <summary>How many students were removed permanently in this run.</summary>
    public int DeletedCount { get; set; }
}
