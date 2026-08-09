using Fahrschule.Application.Students;
using Fahrschule.Domain.Entities;

namespace Fahrschule.Tests.Students;

/// <summary>Tests for the pure training-progress rules.</summary>
public class StudentProgressRulesTests
{
    [Theory]
    [InlineData(null, false)] // simple check-off point
    [InlineData(0, true)]     // voluntary counter (no target)
    [InlineData(1, true)]
    [InlineData(5, true)]
    public void IsCountable_when_a_required_count_is_present(int? required, bool expected)
        => Assert.Equal(expected, StudentProgressRules.IsCountable(required));

    [Fact]
    public void Voluntary_counter_is_never_done_and_not_required()
    {
        var item = new StudentProgressItem { RequiredCount = 0 };
        item.Entries.Add(new StudentProgressEntry());
        Assert.True(StudentProgressRules.IsCountable(item.RequiredCount));
        Assert.False(StudentProgressRules.IsDone(item)); // no target → never "done"
        Assert.False(StudentProgressRules.IsRequired(item)); // optional → off the percentage
    }

    // --- Grundstoff requirement (§ 4 Abs. 3 FahrschAusbO) ---

    [Fact]
    public void Grundstoff_is_twelve_lessons_and_six_with_a_prior_licence()
    {
        // "Der Umfang des allgemeinen Teils (Grundstoff) beträgt mindestens zwölf
        // Doppelstunden [...] Besitzt der Fahrschüler bereits eine Fahrerlaubnis,
        // so beträgt der Umfang mindestens sechs Doppelstunden."
        Assert.Equal(12, StudentProgressRules.RequiredBasicTheoryLessons(
            hasPriorLicense: false, instructorOverride: null, standard: 12, reduced: 6, availableTopics: 12));
        Assert.Equal(6, StudentProgressRules.RequiredBasicTheoryLessons(
            hasPriorLicense: true, instructorOverride: null, standard: 12, reduced: 6, availableTopics: 12));
    }

    [Fact]
    public void An_instructor_override_wins_over_the_derived_number()
    {
        // The escape hatch for what § 4 leaves open (Mofa, foreign licences).
        Assert.Equal(8, StudentProgressRules.RequiredBasicTheoryLessons(
            hasPriorLicense: true, instructorOverride: 8, standard: 12, reduced: 6, availableTopics: 12));
        // 0 or negative is treated as "not set" - the derived value applies.
        Assert.Equal(6, StudentProgressRules.RequiredBasicTheoryLessons(
            hasPriorLicense: true, instructorOverride: 0, standard: 12, reduced: 6, availableTopics: 12));
    }

    [Fact]
    public void The_requirement_never_exceeds_the_topics_the_plan_holds()
    {
        // Otherwise a plan with fewer topics than the target could never be
        // completed and the student would be stuck below 100 %.
        Assert.Equal(9, StudentProgressRules.RequiredBasicTheoryLessons(
            hasPriorLicense: false, instructorOverride: null, standard: 12, reduced: 6, availableTopics: 9));
    }

    [Fact]
    public void IsBasicTheory_only_for_shared_theory_check_off_topics()
    {
        var grundstoff = new StudentProgressItem { Section = "Theorie-Grundstoff", RequiredCount = null };
        Assert.True(StudentProgressRules.IsBasicTheory(grundstoff));

        // Zusatzstoff belongs to a class → not Grundstoff (and § 4 Abs. 4 does
        // not reduce it, so it must never be caught by this rule).
        var zusatzstoff = new StudentProgressItem { Section = "Theorie-Zusatzstoff", RequiredCount = null };
        zusatzstoff.Classes.Add(new StudentProgressItemClass { LicenseClassId = Guid.NewGuid() });
        Assert.False(StudentProgressRules.IsBasicTheory(zusatzstoff));

        // A counter is not a topic, and practice is not theory.
        Assert.False(StudentProgressRules.IsBasicTheory(
            new StudentProgressItem { Section = "Theorie-Zusatzstoff", RequiredCount = 2 }));
        Assert.False(StudentProgressRules.IsBasicTheory(
            new StudentProgressItem { Section = "Praxis", RequiredCount = null }));
    }

    [Fact]
    public void IsDone_simple_point_uses_the_flag()
    {
        var item = new StudentProgressItem { RequiredCount = null, IsCompleted = true };
        Assert.True(StudentProgressRules.IsDone(item));

        item.IsCompleted = false;
        Assert.False(StudentProgressRules.IsDone(item));
    }

    [Fact]
    public void IsDone_countable_point_reaches_target_by_entry_count()
    {
        var item = new StudentProgressItem { RequiredCount = 3 };
        Assert.False(StudentProgressRules.IsDone(item));

        item.Entries.Add(new StudentProgressEntry());
        item.Entries.Add(new StudentProgressEntry());
        Assert.False(StudentProgressRules.IsDone(item)); // 2 of 3

        item.Entries.Add(new StudentProgressEntry());
        Assert.True(StudentProgressRules.IsDone(item)); // 3 of 3
    }

    [Fact]
    public void AppliesToClass_empty_list_means_all_classes()
    {
        var anyClass = Guid.NewGuid();
        Assert.True(StudentProgressRules.AppliesToClass([], anyClass));
    }

    [Fact]
    public void AppliesToClass_checks_membership_when_restricted()
    {
        var b = Guid.NewGuid();
        var a1 = Guid.NewGuid();
        Assert.True(StudentProgressRules.AppliesToClass([b], b));
        Assert.False(StudentProgressRules.AppliesToClass([b], a1));
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(0, 4, 0)]
    [InlineData(1, 4, 25)]
    [InlineData(3, 4, 75)]
    [InlineData(4, 4, 100)]
    public void Percent_rounds_the_share(int done, int total, int expected)
        => Assert.Equal(expected, StudentProgressRules.Percent(done, total));

    [Theory]
    [InlineData("Theorie-Grundstoff", true)]
    [InlineData("Theorie-Zusatzstoff", true)]
    [InlineData("Praxis", false)]
    [InlineData("Sonstiges", false)]
    public void IsTheorySection_matches_the_theorie_prefix(string section, bool expected)
        => Assert.Equal(expected, StudentProgressRules.IsTheorySection(section));

    [Fact]
    public void Theory_topic_expires_after_the_validity_period()
    {
        var taught = new DateOnly(2024, 6, 1);
        // 2-year validity: valid until 2026-06-01.
        Assert.Equal(new DateOnly(2026, 6, 1), StudentProgressRules.TheoryValidUntil(taught, 2));

        // Still valid the day before, expired the day after.
        Assert.False(StudentProgressRules.IsTheoryExpired(taught, 2, new DateOnly(2026, 5, 31)));
        Assert.True(StudentProgressRules.IsTheoryExpired(taught, 2, new DateOnly(2026, 6, 2)));
    }

    [Fact]
    public void Theory_expiry_is_off_when_validity_is_zero_or_no_date()
    {
        Assert.Null(StudentProgressRules.TheoryValidUntil(new DateOnly(2020, 1, 1), 0));
        Assert.Null(StudentProgressRules.TheoryValidUntil(null, 2));
        Assert.False(StudentProgressRules.IsTheoryExpired(new DateOnly(2000, 1, 1), 0, new DateOnly(2026, 1, 1)));
        Assert.False(StudentProgressRules.IsTheoryExpired(null, 2, new DateOnly(2026, 1, 1)));
    }

    [Theory]
    // theoryDone, theoryExam, practiceDone, practiceExam → expected phase
    [InlineData(false, false, false, false, StudentPhase.Theory)]
    [InlineData(true, false, false, false, StudentPhase.TheoryExam)]
    [InlineData(true, true, false, false, StudentPhase.Practice)]
    [InlineData(true, true, true, false, StudentPhase.PracticeExam)]
    [InlineData(true, true, true, true, StudentPhase.Completed)]
    // a passed final exam means done even if earlier item data is incomplete
    [InlineData(false, false, false, true, StudentPhase.Completed)]
    // finished practice drives do NOT advance before the theory exam is passed
    [InlineData(false, false, true, false, StudentPhase.Theory)]
    [InlineData(true, false, true, false, StudentPhase.TheoryExam)]
    public void DerivePhase_picks_the_highest_reached_milestone(
        bool theoryDone, bool theoryExam, bool practiceDone, bool practiceExam, StudentPhase expected)
        => Assert.Equal(expected, StudentProgressRules.DerivePhase(theoryDone, theoryExam, practiceDone, practiceExam));

    [Fact]
    public void RaisePhase_only_ever_moves_up()
    {
        Assert.Equal(StudentPhase.Practice, StudentProgressRules.RaisePhase(StudentPhase.Theory, StudentPhase.Practice));
        // A higher stored (manual) Stand is kept against a lower derived one.
        Assert.Equal(StudentPhase.PracticeExam, StudentProgressRules.RaisePhase(StudentPhase.PracticeExam, StudentPhase.TheoryExam));
    }

    [Theory]
    [InlineData(StudentPhase.Theory, false, false)]
    [InlineData(StudentPhase.TheoryExam, true, false)]
    [InlineData(StudentPhase.Practice, true, false)]
    [InlineData(StudentPhase.PracticeExam, true, true)]
    [InlineData(StudentPhase.Completed, true, true)]
    public void Phase_drives_section_completion(StudentPhase phase, bool theory, bool practice)
    {
        Assert.Equal(theory, StudentProgressRules.TheoryCountsComplete(phase));
        Assert.Equal(practice, StudentProgressRules.PracticeCountsComplete(phase));
    }
}
