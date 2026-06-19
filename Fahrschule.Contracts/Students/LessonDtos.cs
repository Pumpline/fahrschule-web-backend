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

    /// <summary>Start time as "HH:mm" (e.g. "18:00").</summary>
    public string StartTime { get; set; } = string.Empty;

    public int DurationMinutes { get; set; }
    public string? Note { get; set; }

    /// <summary>Titles of the points covered in this lesson (for the list view).</summary>
    public List<string> CoveredTitles { get; set; } = [];

    /// <summary>The points covered in this lesson with their counting state -
    /// used to pre-fill the edit dialog.</summary>
    public List<LessonCoverDto> Covered { get; set; } = [];
}

/// <summary>One point covered by a lesson, with how it was counted.</summary>
public class LessonCoverDto
{
    public Guid ItemId { get; set; }
    public string Title { get; set; } = string.Empty;

    /// <summary>True if the point has a counter (Sonderfahrt etc.).</summary>
    public bool IsCountable { get; set; }

    /// <summary>For a countable point: did this lesson count as a full session
    /// (+1)? false = only practised (recorded but not counted).</summary>
    public bool CountsTowardRequirement { get; set; }
}

/// <summary>"Enter a lesson" request (KONZEPT 3.3).</summary>
public class CreateLessonRequest
{
    /// <summary>"Theory" or "Practice".</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>The class, or null for shared "Grundstoff" (counts for all).</summary>
    public Guid? LicenseClassId { get; set; }

    public DateOnly DateOn { get; set; }

    /// <summary>Start time as "HH:mm" (required, e.g. "18:00").</summary>
    public string StartTime { get; set; } = string.Empty;

    public int DurationMinutes { get; set; }
    public string? Note { get; set; }

    /// <summary>The progress points covered in this lesson (their ids).</summary>
    public Guid[] CoveredItemIds { get; set; } = [];

    /// <summary>Subset of <see cref="CoveredItemIds"/> (countable points) that
    /// were only PRACTISED, not a full session - they are recorded but do NOT
    /// raise the counter (e.g. 30 min Überlandfahrt).</summary>
    public Guid[] PartialPracticeItemIds { get; set; } = [];

    /// <summary>Optional: the calendar appointment this lesson was carried out for
    /// (KONZEPT 3.5). When set, that appointment is marked "durchgeführt" and
    /// linked to this lesson.</summary>
    public Guid? CalendarEventId { get; set; }
}

/// <summary>
/// "Edit a lesson" request: corrects the lesson's own fields (date, start time,
/// duration, note) AND its covered content. Changing the covered points
/// recomputes the affected progress (the lesson is the source of truth). The
/// type and class stay fixed - for those, delete and enter the lesson again.
/// </summary>
public class UpdateLessonRequest
{
    public DateOnly DateOn { get; set; }

    /// <summary>Start time as "HH:mm" (required, e.g. "18:00").</summary>
    public string StartTime { get; set; } = string.Empty;

    public int DurationMinutes { get; set; }
    public string? Note { get; set; }

    /// <summary>The progress points this lesson covers (their ids). Editing this
    /// adds/removes coverage and recomputes the affected progress.</summary>
    public Guid[] CoveredItemIds { get; set; } = [];

    /// <summary>Subset of <see cref="CoveredItemIds"/> (countable points) that
    /// only count as practice, not a full session (+0 instead of +1).</summary>
    public Guid[] PartialPracticeItemIds { get; set; } = [];
}
