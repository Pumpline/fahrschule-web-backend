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

    // --- Lesson entry (KONZEPT 3.3) - editable quick durations, project rule 3 ---

    /// <summary>The duration pre-selected in the "Stunde eintragen" dialog
    /// (minutes, default 90). Any value can still be entered freely.</summary>
    public int LessonDefaultDurationMinutes { get; set; }

    /// <summary>Quick-pick lesson durations as a comma-separated minute list
    /// (e.g. "45, 90, 135, 180"). Shown as buttons in the lesson dialog.</summary>
    public string LessonDurationPresets { get; set; } = string.Empty;

    /// <summary>Default start time ("HH:mm") pre-filled for a THEORY lesson in the
    /// "Stunde eintragen" dialog (theory usually starts at a fixed evening time).
    /// Editable (project rule 3); defaults to "18:00". Practice has no default.</summary>
    public string LessonTheoryDefaultStartTime { get; set; } = string.Empty;

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

    /// <summary>Fahrlehrer number printed in the "FL" column of the training
    /// record. There is a single instructor (the owner), so this pre-fills every
    /// row. Editable (project rule 3); defaults to "01" when left blank.</summary>
    public string SchoolInstructorNumber { get; set; } = string.Empty;

    /// <summary>Name of the instructor behind the FL number (e.g. "Herr Rätze").
    /// Printed in the record's legend ("FL 01 = …") so the number is explained.
    /// Editable; empty means the legend only shows the generic note.</summary>
    public string SchoolInstructorName { get; set; } = string.Empty;
}
