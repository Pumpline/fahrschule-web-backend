namespace Fahrschule.Domain.Entities;

/// <summary>Kind of a calendar appointment (KONZEPT 3.5).</summary>
public enum CalendarEventKind
{
    Practice = 0,
    Theory = 1,
    Exam = 2,
    /// <summary>Anything else - the label is free text (<see cref="CalendarEvent.CustomTitle"/>).</summary>
    Custom = 3,
}

/// <summary>
/// One appointment in the calendar (KONZEPT 3.5): a practical/theory lesson, an
/// exam, or a custom entry. There is currently one Fahrlehrer, so the calendar
/// is global; a later <c>InstructorUserId</c> can assign appointments to a
/// specific instructor. <see cref="Reminded"/> is reserved for the later push
/// reminder ("already reminded" → no double push).
///
/// Double bookings are prevented by an overlap check in the service
/// (KONZEPT 3.5 "Konfliktprüfung").
/// </summary>
public class CalendarEvent
{
    public Guid Id { get; set; }

    public DateOnly DateOn { get; set; }

    public TimeOnly StartTime { get; set; }

    /// <summary>End time; may be null (e.g. an exam at a fixed start).</summary>
    public TimeOnly? EndTime { get; set; }

    public CalendarEventKind Kind { get; set; }

    /// <summary>Free label for <see cref="CalendarEventKind.Custom"/> entries
    /// (German - shown to users). null for the fixed kinds.</summary>
    public string? CustomTitle { get; set; }

    /// <summary>Optional link to a student.</summary>
    public Guid? StudentId { get; set; }
    public Student? Student { get; set; }

    /// <summary>Optional note - what is done (e.g. "Autobahnfahrt", "Thema 7").</summary>
    public string? Note { get; set; }

    /// <summary>Reserved for the push reminder (KONZEPT 3.5) - no double push.</summary>
    public bool Reminded { get; set; }

    /// <summary>Set once the planned lesson was actually carried out and recorded
    /// (KONZEPT 3.5: "geplant vs. durchgeführt"). null = still only planned.</summary>
    public Guid? LessonId { get; set; }
    public Lesson? Lesson { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
