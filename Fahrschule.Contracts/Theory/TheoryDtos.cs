namespace Fahrschule.Contracts.Theory;

/// <summary>A theory topic to choose for a session (current catalogue version).</summary>
public class TheoryTopicDto
{
    public Guid Id { get; set; }
    public Guid ItemKey { get; set; }
    public string Section { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
}

/// <summary>A theory session in the list (newest first).</summary>
public class TheorySessionListItemDto
{
    public Guid Id { get; set; }
    public DateOnly DateOn { get; set; }
    public string TopicTitle { get; set; } = string.Empty;
    public string TopicSection { get; set; } = string.Empty;
    public int AttendeeCount { get; set; }
}

/// <summary>A theory session with its attendees.</summary>
public class TheorySessionDetailDto
{
    public Guid Id { get; set; }
    public DateOnly DateOn { get; set; }
    public int DurationMinutes { get; set; }
    public Guid CurriculumItemKey { get; set; }
    public string TopicTitle { get; set; } = string.Empty;
    public string TopicSection { get; set; } = string.Empty;
    public string? Note { get; set; }
    public List<TheoryAttendeeDto> Attendees { get; set; } = [];
}

/// <summary>One attendee of a theory session.</summary>
public class TheoryAttendeeDto
{
    public Guid StudentId { get; set; }
    public string FullName { get; set; } = string.Empty;
    /// <summary>True if this attendance ticked the topic in the student's progress.</summary>
    public bool CountedProgress { get; set; }
}

/// <summary>Create a theory session (with an optional initial set of attendees).</summary>
public class CreateTheorySessionRequest
{
    public DateOnly DateOn { get; set; }
    public int DurationMinutes { get; set; }
    /// <summary>The chosen theory topic (a current catalogue item).</summary>
    public Guid CurriculumItemId { get; set; }
    public string? Note { get; set; }
    public List<Guid> StudentIds { get; set; } = [];
}

/// <summary>Add one or more attendees to an existing session.</summary>
public class AddAttendeesRequest
{
    public List<Guid> StudentIds { get; set; } = [];
}
