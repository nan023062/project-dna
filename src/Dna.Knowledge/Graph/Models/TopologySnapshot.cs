namespace Dna.Knowledge;

public class TopologySnapshot
{
    public List<KnowledgeNode> Nodes { get; init; } = [];
    public List<KnowledgeEdge> Edges { get; init; } = [];
    public Dictionary<string, List<string>> DepMap { get; init; } = new();
    public Dictionary<string, List<string>> RdepMap { get; init; } = new();
    public List<CrossWork> CrossWorks { get; init; } = [];
    public DateTime BuiltAt { get; init; } = DateTime.UtcNow;
}
