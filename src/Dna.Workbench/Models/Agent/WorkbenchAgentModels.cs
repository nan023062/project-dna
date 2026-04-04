namespace Dna.Workbench.Models.Agent;

public sealed class AgentTaskRequest
{
    public string Title { get; init; } = string.Empty;

    public string Objective { get; init; } = string.Empty;

    public List<string> TargetNodeIds { get; init; } = [];

    public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class AgentSessionSnapshot
{
    public string SessionId { get; init; } = Guid.NewGuid().ToString("N");

    public string Status { get; init; } = WorkbenchAgentConstants.SessionStatus.Pending;

    public DateTime StartedAtUtc { get; init; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; init; } = DateTime.UtcNow;

    public AgentTaskRequest Task { get; init; } = new();

    public List<AgentTimelineEvent> Timeline { get; init; } = [];
}

public sealed class AgentTimelineEvent
{
    public string EventId { get; init; } = Guid.NewGuid().ToString("N");

    public string SessionId { get; init; } = string.Empty;

    public string EventType { get; init; } = WorkbenchAgentConstants.EventTypes.TaskStarted;

    public string? NodeId { get; init; }

    public string? Relation { get; init; }

    public string Message { get; init; } = string.Empty;

    public DateTime OccurredAtUtc { get; init; } = DateTime.UtcNow;

    public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
