namespace Fahrschule.Contracts.Curriculum;

/// <summary>Ein Ausbildungsplan-Punkt (aktuell gültige Version), wie die API ihn zeigt.</summary>
public class CurriculumItemDto
{
    public Guid Id { get; set; }

    /// <summary>Feste Kennung über alle Versionen hinweg.</summary>
    public Guid ItemKey { get; set; }

    /// <summary>Versionsnummer dieser Fassung (1, 2, 3 …).</summary>
    public int Version { get; set; }

    /// <summary>Ab wann diese Fassung gilt.</summary>
    public DateTime ValidFromUtc { get; set; }

    public string Section { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public int? RequiredCount { get; set; }
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }

    /// <summary>Zugeordnete Klassen (leer = gilt für alle Klassen).</summary>
    public Guid[] ClassIds { get; set; } = [];
    public string[] ClassCodes { get; set; } = [];

    /// <summary>Versionsmarke gegen gegenseitiges Überschreiben (siehe LicenseClassDto).</summary>
    public uint RowVersion { get; set; }
}
