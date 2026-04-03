using Dna.Memory.Models;

namespace Dna.Knowledge;

public enum EvolutionKnowledgeLayer
{
    Session,
    Memory,
    Knowledge
}

public sealed class KnowledgeEvolutionSuggestion
{
    public string MemoryId { get; init; } = string.Empty;
    public string? NodeId { get; init; }
    public string? NodeName { get; init; }
    public MemoryType Type { get; init; }
    public MemoryStage Stage { get; init; }
    public EvolutionKnowledgeLayer CurrentLayer { get; init; }
    public EvolutionKnowledgeLayer TargetLayer { get; init; }
    public string? Summary { get; init; }
    public string Reason { get; init; } = string.Empty;
    public double Confidence { get; init; }
    public List<string> Tags { get; init; } = [];
    public List<string> CandidateModuleIds { get; init; } = [];
    public List<string> CandidateModuleNames { get; init; } = [];
}

public sealed class KnowledgeEvolutionReport
{
    public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;
    public string? FilterNodeId { get; init; }
    public string? FilterNodeName { get; init; }
    public int SessionToMemoryCount { get; init; }
    public int MemoryToKnowledgeCount { get; init; }
    public List<KnowledgeEvolutionSuggestion> Suggestions { get; init; } = [];
}
