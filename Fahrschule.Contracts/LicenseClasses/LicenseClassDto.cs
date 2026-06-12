namespace Fahrschule.Contracts.LicenseClasses;

/// <summary>Eine Führerscheinklasse, wie die API sie nach außen zeigt.</summary>
public class LicenseClassDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int? MinimumAge { get; set; }
    public string? Requirements { get; set; }
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }

    /// <summary>
    /// Versionsmarke für die optimistische Nebenläufigkeit (Projektregel 7):
    /// Beim Speichern schickt das Frontend diese Zahl mit. Hat zwischenzeitlich
    /// jemand anderes gespeichert, passt sie nicht mehr und die API antwortet
    /// mit einem verständlichen Konflikt statt Änderungen zu überschreiben.
    /// (Technisch: die xmin-Systemspalte von PostgreSQL.)
    /// </summary>
    public uint Version { get; set; }
}
