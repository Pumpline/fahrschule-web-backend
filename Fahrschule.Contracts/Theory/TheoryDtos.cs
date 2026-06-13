namespace Fahrschule.Contracts.Theory;

/// <summary>A theory topic to choose (current catalogue version, simple check-off).</summary>
public class TheoryTopicDto
{
    public Guid Id { get; set; }
    public Guid ItemKey { get; set; }
    public string Section { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
}

/// <summary>
/// Quick "tick a theory topic for several students at once" request (KONZEPT
/// Stufe 2). This is just a shortcut for manual work - nothing is stored as an
/// attendance list; the only lasting effect is the topic being ticked in each
/// student's theory progress.
/// </summary>
public class TickTheoryRequest
{
    public DateOnly DateOn { get; set; }
    /// <summary>The chosen theory topic (a current catalogue item).</summary>
    public Guid CurriculumItemId { get; set; }
    public List<Guid> StudentIds { get; set; } = [];
}

/// <summary>What the quick tick did, so the UI can give clear feedback.</summary>
public class TheoryTickResultDto
{
    /// <summary>Newly ticked off for this many students.</summary>
    public int Ticked { get; set; }
    /// <summary>Already had this topic done (left unchanged).</summary>
    public int AlreadyDone { get; set; }
    /// <summary>Topic is not part of the student's plan (skipped).</summary>
    public int NotApplicable { get; set; }
}
