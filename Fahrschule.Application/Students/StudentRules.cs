using Fahrschule.Domain.Entities;

namespace Fahrschule.Application.Students;

/// <summary>
/// Pure business rules for students - no database, easy to unit test.
/// </summary>
public static class StudentRules
{
    public const int MaxNameLength = 100;

    /// <summary>Full age in years at <paramref name="onDate"/> for the given
    /// birth date (handles the birthday-not-yet-reached case).</summary>
    public static int AgeInYears(DateOnly birthDate, DateOnly onDate)
    {
        var age = onDate.Year - birthDate.Year;
        if (onDate < birthDate.AddYears(age))
        {
            age--;
        }
        return age;
    }

    /// <summary>
    /// May a student with this birth date register for a class with the given
    /// minimum age, on the given date? null minimum age = no restriction.
    /// We allow registration up to one year before the minimum age, because
    /// training (especially theory) may start before the birthday - the exam
    /// age is checked separately later. Returns the age requirement message
    /// when not allowed, otherwise null.
    /// </summary>
    public static string? CheckMinimumAge(DateOnly? birthDate, int? minimumAge, DateOnly onDate)
    {
        if (minimumAge is null || birthDate is null)
        {
            return null;
        }

        var age = AgeInYears(birthDate.Value, onDate);
        // Training may begin up to 12 months before reaching the minimum age.
        if (age < minimumAge.Value - 1)
        {
            return $"Für diese Klasse ist ein Mindestalter von {minimumAge} Jahren vorgesehen. " +
                   "Die Ausbildung darf frühestens ein Jahr vorher beginnen.";
        }
        return null;
    }

    /// <summary>
    /// A rough progress percentage derived from the phase. This is a STAND-IN
    /// until real lesson/exam tracking arrives (KONZEPT step 4); the student
    /// list only ever shows this aggregate (data minimisation - no details).
    /// </summary>
    public static int ProgressForPhase(StudentPhase phase) => phase switch
    {
        StudentPhase.Theory => 10,
        StudentPhase.TheoryExam => 35,
        StudentPhase.Practice => 60,
        StudentPhase.PracticeExam => 85,
        StudentPhase.Completed => 100,
        _ => 0,
    };

    /// <summary>Average progress across all of a student's classes (0 if none).</summary>
    public static int OverallProgress(IEnumerable<StudentPhase> phases)
    {
        var list = phases.ToList();
        return list.Count == 0 ? 0 : (int)Math.Round(list.Average(ProgressForPhase));
    }
}
