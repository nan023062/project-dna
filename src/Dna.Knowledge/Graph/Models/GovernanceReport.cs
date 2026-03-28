namespace Dna.Knowledge;

public class GovernanceReport
{
    public List<CycleSuggestion> CycleSuggestions { get; init; } = [];
    public List<KnowledgeNode> OrphanNodes { get; init; } = [];
    public List<CrossWorkIssue> CrossWorkIssues { get; init; } = [];
    public List<DependencyDriftIssue> DependencyDrifts { get; init; } = [];
    public List<KeyNodeWarning> KeyNodeWarnings { get; init; } = [];

    public int TotalIssues => CycleSuggestions.Count + OrphanNodes.Count + CrossWorkIssues.Count + DependencyDrifts.Count + KeyNodeWarnings.Count;
    public bool IsHealthy => TotalIssues == 0;
}

public class CycleSuggestion
{
    public List<string> CycleMembers { get; init; } = [];
    public string Message { get; init; } = string.Empty;
    public string Suggestion { get; init; } = string.Empty;
}

public class CrossWorkIssue
{
    public required string CrossWorkId { get; init; }
    public required string CrossWorkName { get; init; }
    public required string Message { get; init; }
}

public class DependencyDriftIssue
{
    public required string ModuleName { get; init; }
    public required string Message { get; init; }
    public List<string> DeclaredOnly { get; init; } = [];
    public List<string> ComputedOnly { get; init; } = [];
    public string? Suggestion { get; init; }
}

public class KeyNodeWarning
{
    public required string NodeName { get; init; }
    public int DependentCount { get; init; }
    public required string Message { get; init; }
}
