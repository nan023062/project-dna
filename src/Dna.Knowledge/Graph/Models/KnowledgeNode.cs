namespace Dna.Knowledge;

public enum NodeType
{
    Project,
    Department,
    Technical,
    Team,
    // Backward-compatible aliases for old terminology.
    Root = Project,
    Module = Technical,
    Group = Technical,
    CrossWork = Team
}

public class KnowledgeNode
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public NodeType Type { get; set; } = NodeType.Technical;

    public string? ParentId { get; set; }
    public List<string> ChildIds { get; set; } = [];

    public List<string> Dependencies { get; set; } = [];
    public List<string> ComputedDependencies { get; set; } = [];
    public List<string> SymbioticPeers { get; set; } = [];

    public string? Contract { get; set; }
    public List<string>? PublicApi { get; set; }
    public List<string>? Constraints { get; set; }

    public string? RelativePath { get; set; }
    public string? Maintainer { get; set; }
    public string? Summary { get; set; }
    public List<string> Keywords { get; set; } = [];
    public string? Boundary { get; set; }
    public string? Discipline { get; set; }
    public bool IsCrossWorkModule { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }

    public NodeKnowledge Knowledge { get; set; } = new();
}

public class NodeKnowledge
{
    public string? Identity { get; set; }
    public List<LessonSummary> Lessons { get; set; } = [];
    public List<string> ActiveTasks { get; set; } = [];
    public List<string> Facts { get; set; } = [];
    public int TotalMemoryCount { get; set; }
    public List<string> MemoryIds { get; set; } = [];
}

public class LessonSummary
{
    public string Title { get; set; } = string.Empty;
    public string? Severity { get; set; }
    public string? Resolution { get; set; }
}
