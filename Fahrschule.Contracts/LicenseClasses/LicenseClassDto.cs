namespace Fahrschule.Contracts.LicenseClasses;

/// <summary>A licence class as the API exposes it.</summary>
public class LicenseClassDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int? MinimumAge { get; set; }
    public string? Requirements { get; set; }
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }

    /// <summary>Class-specific theory double lessons (Zusatzstoff), e.g. B = 2.</summary>
    public int RequiredTheoryDoubleLessons { get; set; }

    /// <summary>Mandatory special drives per class (drive the progress counters).</summary>
    public int RequiredSpecialDrivesOverland { get; set; }
    public int RequiredSpecialDrivesHighway { get; set; }
    public int RequiredSpecialDrivesNight { get; set; }

    /// <summary>
    /// Version marker for optimistic concurrency (project rule 7):
    /// the frontend sends this number back when saving. If someone else
    /// saved in the meantime, it no longer matches and the API responds
    /// with an understandable conflict instead of overwriting changes.
    /// (Technically: PostgreSQL's xmin system column.)
    /// </summary>
    public uint Version { get; set; }
}
