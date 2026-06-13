namespace Fahrschule.Domain.Entities;

/// <summary>
/// A follow-up / reminder ("Wiedervorlage", KONZEPT Stufe 2): a free-text task
/// with a due date, optionally linked to a student (e.g. "Antrag läuft ab",
/// "Prüfung anmelden"). Operational data - the office may hard-delete it, and it
/// carries no special categories. When it is linked to a student, the retention
/// job removes it together with that student.
/// </summary>
public class Reminder
{
    public Guid Id { get; set; }

    /// <summary>What to do (German, user text).</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Optional longer note.</summary>
    public string? Note { get; set; }

    /// <summary>The day the follow-up is due.</summary>
    public DateOnly DueOn { get; set; }

    /// <summary>Optional link to a student.</summary>
    public Guid? StudentId { get; set; }
    public Student? Student { get; set; }

    public bool IsDone { get; set; }

    /// <summary>When it was marked done (null while open).</summary>
    public DateTime? DoneAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
