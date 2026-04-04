namespace Dna.Workbench.Runtime;

public sealed class WorkbenchRuntimeEvent
{
    public string EventId { get; init; } = Guid.NewGuid().ToString("N");

    public string SessionId { get; init; } = string.Empty;

    public string SourceKind { get; init; } = WorkbenchRuntimeConstants.SourceKinds.Unknown;

    public string SourceId { get; init; } = string.Empty;

    public string EventType { get; init; } = WorkbenchRuntimeConstants.EventTypes.TaskStarted;

    public string? NodeId { get; init; }

    public string? Relation { get; init; }

    public string Message { get; init; } = string.Empty;

    public DateTime OccurredAtUtc { get; init; } = DateTime.UtcNow;

    public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
