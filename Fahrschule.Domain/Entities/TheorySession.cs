namespace Fahrschule.Domain.Entities;

/// <summary>
/// One theory double lesson ("Theorie-Doppelstunde", KONZEPT Stufe 2): a date, a
/// topic from the editable theory catalogue, and the students who attended. Many
/// students attend the same session, so this is a GROUP record - the counterpart
/// of the per-student lesson entry.
///
/// The topic is snapshotted (key + title + section) so the attendance proof stays
/// readable even if the catalogue topic is later renamed or superseded.
/// </summary>
public class TheorySession
{
    public Guid Id { get; set; }

    public DateOnly DateOn { get; set; }

    /// <summary>Length in minutes (a Doppelstunde is 90).</summary>
    public int DurationMinutes { get; set; }

    /// <summary>The theory topic, by its version-stable curriculum key.</summary>
    public Guid CurriculumItemKey { get; set; }

    /// <summary>Topic title at the time (German - shown to users / on the proof).</summary>
    public string TopicTitle { get; set; } = string.Empty;

    /// <summary>Topic section, e.g. "Theorie-Grundstoff".</summary>
    public string TopicSection { get; set; } = string.Empty;

    public string? Note { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public List<TheoryAttendance> Attendances { get; set; } = [];
}

/// <summary>
/// One student's attendance at a <see cref="TheorySession"/>. Marking a student
/// present also ticks the topic in their personal theory checklist; we remember
/// exactly which progress point was ticked so removing the attendance can undo
/// precisely that (and nothing a different source completed).
/// </summary>
public class TheoryAttendance
{
    public Guid TheorySessionId { get; set; }
    public TheorySession? Session { get; set; }

    public Guid StudentId { get; set; }
    public Student? Student { get; set; }

    /// <summary>The progress point this attendance ticked off, or null if the
    /// topic was already done / not in the student's plan (then nothing to undo).</summary>
    public Guid? TickedProgressItemId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
