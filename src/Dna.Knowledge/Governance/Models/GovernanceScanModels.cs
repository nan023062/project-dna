namespace Dna.Knowledge;

public enum GovernanceCadence
{
    HighFrequency,
    LowFrequency
}

public enum GovernanceScopeKind
{
    ActiveChanges,
    Module,
    Subtree,
    Global
}

public sealed class GovernanceScanRequest
{
    public GovernanceCadence Cadence { get; init; } = GovernanceCadence.HighFrequency;
    public GovernanceScopeKind Scope { get; init; } = GovernanceScopeKind.ActiveChanges;
    public string? NodeIdOrName { get; init; }
    public TimeSpan ActiveWindow { get; init; } = TimeSpan.FromDays(1);
    public bool IncludeDirectDependencies { get; init; } = true;
    public int MaxModules { get; init; } = 50;
    public int MaxSuggestions { get; init; } = 50;
}

public sealed class GovernanceActiveModule
{
    public string ModuleId { get; init; } = string.Empty;
    public string ModuleName { get; init; } = string.Empty;
    public int RecentMemoryCount { get; init; }
    public List<string> RecentMemoryIds { get; init; } = [];
    public List<string> Reasons { get; init; } = [];
}

public sealed class GovernanceCandidateModule
{
    public string ModuleId { get; init; } = string.Empty;
    public string ModuleName { get; init; } = string.Empty;
    public bool IsDirectlyActive { get; init; }
    public bool AddedByDependencyExpansion { get; init; }
    public List<string> Reasons { get; init; } = [];
}

public sealed class GovernanceScanResult
{
    public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;
    public GovernanceCadence Cadence { get; init; } = GovernanceCadence.HighFrequency;
    public GovernanceScopeKind Scope { get; init; } = GovernanceScopeKind.ActiveChanges;
    public string? ScopeNodeId { get; init; }
    public string? ScopeNodeName { get; init; }
    public List<GovernanceActiveModule> ActiveModules { get; init; } = [];
    public List<GovernanceCandidateModule> CandidateModules { get; init; } = [];
    public GovernanceReport ArchitectureReport { get; init; } = new();
    public KnowledgeEvolutionReport EvolutionReport { get; init; } = new();
}
