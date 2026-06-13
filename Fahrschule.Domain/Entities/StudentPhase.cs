namespace Fahrschule.Domain.Entities;

/// <summary>
/// The training phase of a student WITHIN ONE licence class (KONZEPT 3.1:
/// the status lives per class, not per student - a student can be in
/// different phases for different classes).
///
/// The numeric values define the order (and let us derive a rough progress
/// percentage until real lesson tracking arrives in a later step).
/// </summary>
public enum StudentPhase
{
    /// <summary>Learning theory.</summary>
    Theory = 0,

    /// <summary>Theory done, taking / waiting for the theory exam.</summary>
    TheoryExam = 1,

    /// <summary>Theory passed, doing practical driving lessons.</summary>
    Practice = 2,

    /// <summary>Practice done, taking / waiting for the practical exam.</summary>
    PracticeExam = 3,

    /// <summary>Finished - licence class completed.</summary>
    Completed = 4,
}
