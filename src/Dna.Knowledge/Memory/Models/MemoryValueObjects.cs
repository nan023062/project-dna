using Dna.Knowledge;

namespace Dna.Memory.Models;

public sealed class MemoryContent
{
    public string Body { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public double Importance { get; set; } = 0.5;
    public List<string> Tags { get; set; } = [];
}

public sealed class MemoryAddress
{
    public NodeType NodeType { get; set; } = NodeType.Technical;
    public string? NodeId { get; set; }
    public List<string> Disciplines { get; set; } = [];
    public List<string> Features { get; set; } = [];
    public List<string> PathPatterns { get; set; } = [];
    public string? ParentMemoryId { get; set; }
    public List<string> RelatedMemoryIds { get; set; } = [];
}

public sealed class MemoryLifecycle
{
    public MemoryStage Stage { get; set; } = MemoryStage.LongTerm;
    public FreshnessStatus Freshness { get; set; } = FreshnessStatus.Fresh;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastVerifiedAt { get; set; }
    public DateTime? StaleAfter { get; set; }
    public string? SupersededBy { get; set; }
    public int Version { get; set; } = 1;
}
