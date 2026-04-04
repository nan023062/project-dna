using Dna.Knowledge;

namespace Dna.Workbench.Governance;

public sealed class WorkbenchGovernanceRequest
{
    public GovernanceCadence Cadence { get; init; } = GovernanceCadence.HighFrequency;
    public GovernanceScopeKind Scope { get; init; } = GovernanceScopeKind.ActiveChanges;
    public string? NodeIdOrName { get; init; }
    public TimeSpan ActiveWindow { get; init; } = TimeSpan.FromDays(1);
    public bool IncludeDirectDependencies { get; init; } = true;
    public int MaxModules { get; init; } = 50;
    public int MaxSuggestions { get; init; } = 50;
}

public sealed class WorkbenchGovernanceContext
{
    public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;
    public GovernanceCadence Cadence { get; init; } = GovernanceCadence.HighFrequency;
    public GovernanceScopeKind Scope { get; init; } = GovernanceScopeKind.ActiveChanges;
    public string? ScopeNodeId { get; init; }
    public string? ScopeNodeName { get; init; }
    public IReadOnlyList<WorkbenchGovernanceRule> Rules { get; init; } = [];
    public IReadOnlyList<GovernanceActiveModule> ActiveModules { get; init; } = [];
    public IReadOnlyList<WorkbenchGovernanceModuleContext> Modules { get; init; } = [];
    public GovernanceReport ArchitectureReport { get; init; } = new();
    public KnowledgeEvolutionReport EvolutionReport { get; init; } = new();
}

public sealed class WorkbenchGovernanceModuleContext
{
    public string NodeId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public NodeType Type { get; init; } = NodeType.Technical;
    public string? Discipline { get; init; }
    public string? ParentId { get; init; }
    public int Layer { get; init; }
    public string? Summary { get; init; }
    public string? Boundary { get; init; }
    public IReadOnlyList<string> ManagedPaths { get; init; } = [];
    public IReadOnlyList<string> DeclaredDependencies { get; init; } = [];
    public IReadOnlyList<string> ComputedDependencies { get; init; } = [];
    public IReadOnlyList<string> PublicApi { get; init; } = [];
    public IReadOnlyList<string> Constraints { get; init; } = [];
    public string? KnowledgeIdentity { get; init; }
    public IReadOnlyList<string> KnowledgeFacts { get; init; } = [];
    public IReadOnlyList<string> KnowledgeActiveTasks { get; init; } = [];
    public bool IsDirectlyActive { get; init; }
    public bool AddedByDependencyExpansion { get; init; }
    public IReadOnlyList<string> GovernanceReasons { get; init; } = [];
}

public sealed class WorkbenchGovernanceRule
{
    public string Code { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
}
