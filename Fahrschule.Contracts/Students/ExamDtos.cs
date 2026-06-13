namespace Fahrschule.Contracts.Students;

/// <summary>A student's exams plus the active repeat locks (KONZEPT 3.4).</summary>
public class ExamListDto
{
    public List<ExamDto> Exams { get; set; } = [];
    public List<ExamLockDto> Locks { get; set; } = [];
}

/// <summary>One exam of a student.</summary>
public class ExamDto
{
    public Guid Id { get; set; }

    /// <summary>"Theory" or "Practice".</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>true = "Vorprüfung" (only noted, no attempt, no lock).</summary>
    public bool IsPreliminary { get; set; }

    public Guid LicenseClassId { get; set; }
    public string ClassCode { get; set; } = string.Empty;

    public DateOnly DateOn { get; set; }

    /// <summary>"Planned", "Passed" or "Failed".</summary>
    public string Result { get; set; } = string.Empty;

    /// <summary>Attempt number for real exams (1, 2, ...); null for preliminary.</summary>
    public int? AttemptNumber { get; set; }

    public string? Note { get; set; }
}

/// <summary>An active repeat lock after a failed exam (KONZEPT 3.4).</summary>
public class ExamLockDto
{
    public Guid LicenseClassId { get; set; }
    public string ClassCode { get; set; } = string.Empty;

    /// <summary>"Theory" or "Practice".</summary>
    public string Kind { get; set; } = string.Empty;

    public DateOnly FailedOn { get; set; }
    public DateOnly LockedUntil { get; set; }

    /// <summary>true = the shortened lock applies (enough lessons logged).</summary>
    public bool IsShortened { get; set; }

    /// <summary>Qualifying lessons logged since the failed exam.</summary>
    public int LessonsSince { get; set; }

    /// <summary>Lessons needed to shorten the lock.</summary>
    public int LessonsNeeded { get; set; }

    public int NormalWeeks { get; set; }
    public int ShortenedWeeks { get; set; }
}

/// <summary>"Enter an exam" request (KONZEPT 3.4).</summary>
public class CreateExamRequest
{
    /// <summary>"Theory" or "Practice".</summary>
    public string Kind { get; set; } = string.Empty;

    public bool IsPreliminary { get; set; }

    public Guid LicenseClassId { get; set; }

    public DateOnly DateOn { get; set; }

    /// <summary>"Planned", "Passed" or "Failed".</summary>
    public string Result { get; set; } = string.Empty;

    public string? Note { get; set; }
}
