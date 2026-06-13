namespace Fahrschule.Contracts.Reminders;

/// <summary>A follow-up / reminder ("Wiedervorlage", KONZEPT Stufe 2).</summary>
public class ReminderDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Note { get; set; }
    public DateOnly DueOn { get; set; }

    public Guid? StudentId { get; set; }
    /// <summary>Name of the linked student (null if none / hidden).</summary>
    public string? StudentName { get; set; }

    public bool IsDone { get; set; }
    public DateTime? DoneAtUtc { get; set; }
}

/// <summary>Input for creating / updating a follow-up.</summary>
public class SaveReminderRequest
{
    public string Title { get; set; } = string.Empty;
    public string? Note { get; set; }
    public DateOnly DueOn { get; set; }
    public Guid? StudentId { get; set; }
}
