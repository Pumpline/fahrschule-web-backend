namespace Fahrschule.Domain.Entities;

/// <summary>
/// A single configuration value (key → value).
///
/// Why: Project rule 3 - everything that can change for business reasons
/// (retention periods, reminder lead times, exam lock durations ...) is
/// maintained as DATA, not hard-coded. The admin panel will edit exactly
/// this table.
/// </summary>
public class Setting
{
    /// <summary>Unique key, e.g. "Erinnerung.VorlaufMinuten".</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>The value as text; business logic converts it as needed.</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>Explanation for the admin panel describing what this value does.</summary>
    public string? Description { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
