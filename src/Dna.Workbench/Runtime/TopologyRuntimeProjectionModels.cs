namespace Dna.Workbench.Runtime;

public sealed class TopologyRuntimeProjectionSnapshot
{
    public string SessionId { get; init; } = string.Empty;

    public List<TopologyRuntimeNodeState> Nodes { get; init; } = [];

    public List<TopologyRuntimeEdgeState> Edges { get; init; } = [];

    public DateTime UpdatedAtUtc { get; init; } = DateTime.UtcNow;
}

public sealed class TopologyRuntimeNodeState
{
    public string NodeId { get; init; } = string.Empty;

    public string State { get; init; } = string.Empty;

    public string Caption { get; init; } = string.Empty;

    public double Heat { get; init; }
}

public sealed class TopologyRuntimeEdgeState
{
    public string FromNodeId { get; init; } = string.Empty;

    public string ToNodeId { get; init; } = string.Empty;

    public string Relation { get; init; } = string.Empty;

    public string State { get; init; } = string.Empty;

    public double Heat { get; init; }
}
