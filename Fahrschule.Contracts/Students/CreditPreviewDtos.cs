namespace Fahrschule.Contracts.Students;

/// <summary>
/// Preview of what adding a licence class would mean for a student
/// (KONZEPT 3.3a, "Anrechnung"). Compares today's plan for the candidate class
/// with what the student has already completed.
/// </summary>
public class CreditPreviewDto
{
    public Guid LicenseClassId { get; set; }
    public string Code { get; set; } = string.Empty;

    /// <summary>Already done and unchanged since → credited automatically.</summary>
    public List<CreditPreviewItemDto> AlreadyCredited { get; set; } = [];

    /// <summary>Already done, but the plan content changed since (newer version)
    /// → the Fahrlehrer should check whether it still counts.</summary>
    public List<CreditPreviewItemDto> NeedsReview { get; set; } = [];

    /// <summary>Still open for this class (class-specific or not yet done).</summary>
    public List<CreditPreviewItemDto> NewPoints { get; set; } = [];
}

/// <summary>One point in the credit preview.</summary>
public class CreditPreviewItemDto
{
    public string Section { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
}
