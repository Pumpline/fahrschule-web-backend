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

    /// <summary>
    /// Retention period (in YEARS) for a student's records after the end of the
    /// year in which the training finished. After this the retention job removes
    /// the data permanently (§ 31 Abs. 3 FahrlG / KONZEPT rule 7). Data, not code
    /// (rule 3): the owner adjusts it if the law changes. Default 5.
    /// </summary>
    public int RetentionStudentYears { get; set; }

    // --- Driving-school master data (KONZEPT 1b) - shown on the printed
    //     Ausbildungsnachweis. Free text, editable in the admin panel. ---

    /// <summary>Name of the driving school, e.g. "Fahrschule Muster".</summary>
    public string SchoolName { get; set; } = string.Empty;

    /// <summary>Street and house number.</summary>
    public string SchoolStreet { get; set; } = string.Empty;

    public string SchoolPostalCode { get; set; } = string.Empty;
    public string SchoolCity { get; set; } = string.Empty;

    /// <summary>Official permit number (Erlaubnisnummer).</summary>
    public string SchoolPermitNumber { get; set; } = string.Empty;
}
