namespace Fahrschule.Contracts.Students;

/// <summary>A student's training progress, grouped per licence class
/// (KONZEPT 3.3: "Fortschritt pro Führerscheinklasse sichtbar").</summary>
public class StudentProgressDto
{
    public List<ClassProgressDto> Classes { get; set; } = [];
}

/// <summary>Progress for one of the student's licence classes.</summary>
public class ClassProgressDto
{
    public Guid LicenseClassId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Phase of this class (Theory/Practice/...), as the enum name.</summary>
    public string Phase { get; set; } = string.Empty;

    public int DoneCount { get; set; }
    public int TotalCount { get; set; }

    /// <summary>Share of completed points for this class (0..100).</summary>
    public int DonePercent { get; set; }

    /// <summary>The plan points of this class, grouped by section.</summary>
    public List<ProgressSectionDto> Sections { get; set; } = [];
}

/// <summary>A section of the plan (e.g. "Theorie-Grundstoff") with its points.</summary>
public class ProgressSectionDto
{
    public string Section { get; set; } = string.Empty;
    public List<ProgressItemDto> Items { get; set; } = [];
}

/// <summary>One point of the student's personal checklist.</summary>
public class ProgressItemDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;

    /// <summary>Target count for countable points; null = simple check-off.</summary>
    public int? RequiredCount { get; set; }

    /// <summary>True if this is a countable point (has a +/- counter).</summary>
    public bool IsCountable { get; set; }

    /// <summary>How many sessions are counted so far (countable points).</summary>
    public int CurrentCount { get; set; }

    /// <summary>Done? Simple points: set directly. Countable: reached the target.</summary>
    public bool IsDone { get; set; }

    public DateOnly? CompletedOn { get; set; }
    public string? Note { get; set; }

    /// <summary>Simple point completed by a MANUAL mark (Anrechnung/Übernahme or
    /// theory attendance) rather than a recorded lesson - the UI shows a small
    /// "manuell" hint.</summary>
    public bool CompletedManually { get; set; }

    /// <summary>This point is covered by at least one recorded lesson. When true
    /// the manual haken is locked - the point must be changed via the lesson.</summary>
    public bool CoveredByLesson { get; set; }

    /// <summary>True if this is a THEORY topic whose validity has lapsed: it was
    /// completed more than the configured number of years ago and must be taught
    /// again. While expired it no longer counts and <see cref="IsDone"/> is false.</summary>
    public bool IsExpired { get; set; }

    /// <summary>For a completed theory topic with expiry enabled: the date its
    /// validity ends (last taught + validity years). Lets the UI warn before and
    /// after expiry. Null when expiry does not apply.</summary>
    public DateOnly? ExpiresOn { get; set; }

    /// <summary>True if this point counts for more than one of the student's
    /// classes (a shared "Grundstoff" point) - the UI hints "zählt für mehrere".</summary>
    public bool IsShared { get; set; }

    /// <summary>The counted sessions (countable points only).</summary>
    public List<ProgressEntryDto> Entries { get; set; } = [];
}

/// <summary>One counted session of a countable point.</summary>
public class ProgressEntryDto
{
    public Guid Id { get; set; }
    public DateOnly PerformedOn { get; set; }
    public string? Note { get; set; }
}

/// <summary>"Tick / untick a simple point" request (KONZEPT 3.3).</summary>
public class SetProgressItemRequest
{
    public bool IsDone { get; set; }

    /// <summary>"Erledigt am" - optional; defaults to today when ticking.</summary>
    public DateOnly? CompletedOn { get; set; }

    public string? Note { get; set; }
}

/// <summary>"Add one counted session" request for a countable point.</summary>
public class AddProgressEntryRequest
{
    public DateOnly PerformedOn { get; set; }
    public string? Note { get; set; }
}
