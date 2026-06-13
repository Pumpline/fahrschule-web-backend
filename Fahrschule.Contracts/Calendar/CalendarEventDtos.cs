namespace Fahrschule.Contracts.Calendar;

/// <summary>One appointment in the calendar (KONZEPT 3.5).</summary>
public class CalendarEventDto
{
    public Guid Id { get; set; }
    public DateOnly DateOn { get; set; }

    /// <summary>Start as "HH:mm".</summary>
    public string StartTime { get; set; } = string.Empty;

    /// <summary>End as "HH:mm", or null.</summary>
    public string? EndTime { get; set; }

    /// <summary>"Practice", "Theory", "Exam" or "Custom".</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Display label (kind label, or the custom title).</summary>
    public string Title { get; set; } = string.Empty;

    public Guid? StudentId { get; set; }
    public string? StudentName { get; set; }

    public string? Note { get; set; }
}

/// <summary>"Create / update an appointment" request (KONZEPT 3.5).</summary>
public class SaveCalendarEventRequest
{
    public DateOnly DateOn { get; set; }

    /// <summary>Start as "HH:mm".</summary>
    public string StartTime { get; set; } = string.Empty;

    /// <summary>End as "HH:mm" (optional).</summary>
    public string? EndTime { get; set; }

    /// <summary>"Practice", "Theory", "Exam" or "Custom".</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Required for "Custom" - the free label.</summary>
    public string? CustomTitle { get; set; }

    public Guid? StudentId { get; set; }

    public string? Note { get; set; }
}
