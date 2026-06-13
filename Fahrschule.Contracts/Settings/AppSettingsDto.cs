namespace Fahrschule.Contracts.Settings;

/// <summary>
/// The editable operational settings (KONZEPT 1b/3.4) - reminder lead times
/// and the exam retry-lock values. Everything here is data, not code
/// (project rule 3): the owner adjusts these in the admin panel.
/// </summary>
public class AppSettingsDto
{
    /// <summary>Days before a document expires when it is highlighted / shown
    /// under "Bald fällig" (KONZEPT 3.1, default 21).</summary>
    public int DocumentExpiryReminderDays { get; set; }

    /// <summary>Minutes before an appointment the instructor is reminded via
    /// push (KONZEPT push section, default 30).</summary>
    public int AppointmentReminderLeadMinutes { get; set; }

    /// <summary>Normal retry lock after a failed exam, in weeks (default 2).</summary>
    public int ExamLockNormalWeeks { get; set; }

    /// <summary>Shortened retry lock when extra practice was done, in weeks (default 1).</summary>
    public int ExamLockShortenedWeeks { get; set; }

    /// <summary>Number of extra practice lessons needed to earn the shortened
    /// lock (default 2).</summary>
    public int ExamLockPracticeLessonsForShortening { get; set; }
}
