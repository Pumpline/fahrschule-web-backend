using Fahrschule.Application.Retention;

namespace Fahrschule.Tests.Retention;

/// <summary>
/// Pure date maths for the retention deadline (§ 31 Abs. 3 FahrlG): five years
/// after the end of the training year, then deletable.
/// </summary>
public class StudentRetentionRulesTests
{
    [Fact]
    public void TrainingEnd_is_the_latest_of_registration_lesson_and_exam()
    {
        var reg = new DateOnly(2020, 1, 1);
        var lesson = new DateOnly(2021, 6, 1);
        var exam = new DateOnly(2022, 3, 1);

        Assert.Equal(exam, StudentRetentionRules.TrainingEndDate(reg, lesson, exam));
        Assert.Equal(lesson, StudentRetentionRules.TrainingEndDate(reg, lesson, null));
        Assert.Equal(reg, StudentRetentionRules.TrainingEndDate(reg, null, null));
        // Registration is the floor even if it is later than recorded activity.
        Assert.Equal(new DateOnly(2025, 1, 1),
            StudentRetentionRules.TrainingEndDate(new DateOnly(2025, 1, 1), lesson, exam));
    }

    [Fact]
    public void DeletionDueDate_is_the_first_january_after_the_frist()
    {
        // Training ends 2026, 5 years → keep through 2031, deletable from 2032-01-01.
        Assert.Equal(new DateOnly(2032, 1, 1),
            StudentRetentionRules.DeletionDueDate(new DateOnly(2026, 5, 1), 5));
    }

    [Fact]
    public void IsDue_only_from_the_due_date_on()
    {
        var end = new DateOnly(2026, 12, 31);
        Assert.False(StudentRetentionRules.IsDue(new DateOnly(2031, 12, 31), end, 5));
        Assert.True(StudentRetentionRules.IsDue(new DateOnly(2032, 1, 1), end, 5));
        Assert.True(StudentRetentionRules.IsDue(new DateOnly(2033, 6, 1), end, 5));
    }
}
