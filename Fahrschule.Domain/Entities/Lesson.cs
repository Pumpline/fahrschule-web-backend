namespace Fahrschule.Domain.Entities;

/// <summary>Whether a lesson was theory or practical driving (KONZEPT 3.3).</summary>
public enum LessonType
{
    Theory = 0,
    Practice = 1,
}

/// <summary>
/// One recorded teaching unit for a student (KONZEPT 3.3): the Fahrlehrer enters
/// it from the student file - theory or practice, for which class (or shared
/// "Grundstoff" that counts for all), the date, the duration, and which
/// curriculum points were covered. Saving the lesson applies its effect to the
/// student's progress (ticks simple points, counts countable ones).
///
/// This is the single place where lessons are entered. The covered points are
/// linked via <see cref="Items"/> so the lesson can later appear on the printed
/// Ausbildungsnachweis.
/// </summary>
public class Lesson
{
    public Guid Id { get; set; }

    public Guid StudentId { get; set; }
    public Student? Student { get; set; }

    public LessonType Type { get; set; }

    /// <summary>The class this lesson is for. null = shared "Grundstoff" that
    /// counts for all of the student's classes.</summary>
    public Guid? LicenseClassId { get; set; }
    public LicenseClass? LicenseClass { get; set; }

    /// <summary>The day the lesson took place.</summary>
    public DateOnly DateOn { get; set; }

    /// <summary>Duration in minutes (e.g. 90 for a Doppelstunde).</summary>
    public int DurationMinutes { get; set; }

    /// <summary>Optional note for the whole lesson.</summary>
    public string? Note { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>The curriculum points covered in this lesson.</summary>
    public List<LessonItem> Items { get; set; } = [];
}

/// <summary>Link between a lesson and one covered progress point.</summary>
public class LessonItem
{
    public Guid LessonId { get; set; }
    public Lesson? Lesson { get; set; }

    public Guid StudentProgressItemId { get; set; }
    public StudentProgressItem? StudentProgressItem { get; set; }
}
