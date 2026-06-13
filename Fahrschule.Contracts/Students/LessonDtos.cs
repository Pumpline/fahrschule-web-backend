namespace Fahrschule.Contracts.Students;

/// <summary>One recorded teaching unit of a student (KONZEPT 3.3).</summary>
public class LessonDto
{
    public Guid Id { get; set; }

    /// <summary>"Theory" or "Practice" (the enum name).</summary>
    public string Type { get; set; } = string.Empty;

    public Guid? LicenseClassId { get; set; }

    /// <summary>Class code, or "Grundstoff" when it counts for all classes.</summary>
    public string ClassLabel { get; set; } = string.Empty;

    public DateOnly DateOn { get; set; }
    public int DurationMinutes { get; set; }
    public string? Note { get; set; }

    /// <summary>Titles of the points covered in this lesson.</summary>
    public List<string> CoveredTitles { get; set; } = [];
}

/// <summary>"Enter a lesson" request (KONZEPT 3.3).</summary>
public class CreateLessonRequest
{
    /// <summary>"Theory" or "Practice".</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>The class, or null for shared "Grundstoff" (counts for all).</summary>
    public Guid? LicenseClassId { get; set; }

    public DateOnly DateOn { get; set; }
    public int DurationMinutes { get; set; }
    public string? Note { get; set; }

    /// <summary>The progress points covered in this lesson (their ids).</summary>
    public Guid[] CoveredItemIds { get; set; } = [];

    /// <summary>Optional: the calendar appointment this lesson was carried out for
    /// (KONZEPT 3.5). When set, that appointment is marked "durchgeführt" and
    /// linked to this lesson.</summary>
    public Guid? CalendarEventId { get; set; }
}
