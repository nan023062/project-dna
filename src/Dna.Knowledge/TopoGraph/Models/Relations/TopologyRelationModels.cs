namespace Dna.Knowledge.TopoGraph.Models.Relations;

public enum TopologyRelationKind
{
    Containment,
    Dependency,
    Collaboration
}

public sealed class TopologyRelation
{
    public string FromId { get; init; } = string.Empty;
    public string ToId { get; init; } = string.Empty;
    public TopologyRelationKind Kind { get; init; }
    public bool IsComputed { get; init; }
    public string? Label { get; init; }
}
