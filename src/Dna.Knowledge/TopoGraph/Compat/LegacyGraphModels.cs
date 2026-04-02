namespace Dna.Knowledge;

public enum ContextLevel
{
    Current,
    SharedOrSoft,
    CrossWorkPeer,
    Unlinked
}

public class CrossWork
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? Feature { get; init; }
    public List<CrossWorkParticipant> Participants { get; init; } = [];
}

public class CrossWorkParticipant
{
    public string ModuleName { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public string? Contract { get; init; }
    public string? ContractType { get; init; }
    public string? Deliverable { get; init; }
}

public class ExecutionPlan
{
    public List<string> OrderedModules { get; init; } = [];
    public bool HasCycle { get; init; }
    public string? CycleDescription { get; init; }
}

public class GovernanceReport
{
    public List<CycleSuggestion> CycleSuggestions { get; init; } = [];
    public List<KnowledgeNode> OrphanNodes { get; init; } = [];
    public List<CrossWorkIssue> CrossWorkIssues { get; init; } = [];
    public List<DependencyDriftIssue> DependencyDrifts { get; init; } = [];
    public List<KeyNodeWarning> KeyNodeWarnings { get; init; } = [];

    public int TotalIssues =>
        CycleSuggestions.Count + OrphanNodes.Count + CrossWorkIssues.Count + DependencyDrifts.Count + KeyNodeWarnings.Count;

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

public enum TopologyRelationType
{
    Containment,
    Dependency,
    Collaboration
}

public sealed class TopologyRelation
{
    public string FromId { get; init; } = string.Empty;
    public string ToId { get; init; } = string.Empty;
    public TopologyRelationType Type { get; init; } = TopologyRelationType.Dependency;
    public bool IsComputed { get; init; }
    public string? Label { get; init; }
}

public sealed class KnowledgeEdge
{
    public string From { get; init; } = string.Empty;
    public string To { get; init; } = string.Empty;
    public bool IsComputed { get; init; }
}

public enum NodeType
{
    Project,
    Department,
    Technical,
    Team
}

public sealed class ModuleContract
{
    public string? Boundary { get; set; }
    public List<string> PublicApi { get; set; } = [];
    public List<string> Constraints { get; set; } = [];
}

public sealed class ModulePathBinding
{
    public string? MainPath { get; set; }
    public List<string> ManagedPaths { get; set; } = [];
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
    public ModuleContract ContractInfo { get; set; } = new();
    public ModulePathBinding PathBinding { get; set; } = new();
    public string? Contract { get; set; }

    public List<string>? PublicApi
    {
        get => ContractInfo.PublicApi;
        set => ContractInfo.PublicApi = value ?? [];
    }

    public List<string>? Constraints
    {
        get => ContractInfo.Constraints;
        set => ContractInfo.Constraints = value ?? [];
    }

    public string? RelativePath
    {
        get => PathBinding.MainPath;
        set => PathBinding.MainPath = value;
    }

    public int Layer { get; set; }

    public List<string> ManagedPathScopes
    {
        get => PathBinding.ManagedPaths;
        set => PathBinding.ManagedPaths = value ?? [];
    }

    public string? Maintainer { get; set; }
    public string? Summary { get; set; }
    public List<string> Keywords { get; set; } = [];

    public string? Boundary
    {
        get => ContractInfo.Boundary;
        set => ContractInfo.Boundary = value;
    }

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

public class ModuleContext
{
    public string ModuleName { get; init; } = string.Empty;
    public string? Discipline { get; init; }
    public ContextLevel Level { get; init; }
    public string? IdentityContent { get; init; }
    public string? LessonsContent { get; init; }
    public string? LinksContent { get; init; }
    public string? ActiveContent { get; init; }
    public string? ContractContent { get; init; }
    public List<string> ContentFilePaths { get; init; } = [];
    public string? BlockMessage { get; init; }
    public string? Summary { get; init; }
    public string? Boundary { get; init; }
    public List<string>? PublicApi { get; init; }
    public List<string>? Constraints { get; init; }
    public Dictionary<string, string>? Metadata { get; init; }
    public bool IsBlocked => Level == ContextLevel.Unlinked;
}

public sealed class ModulesManifest
{
    public Dictionary<string, List<ModuleRegistration>> Disciplines { get; set; } = new();
    public List<CrossWorkRegistration> CrossWorks { get; set; } = [];
    public Dictionary<string, FeatureDefinition> Features { get; set; } = new();
}

public sealed class LayerDefinition
{
    public int Level { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class ModuleRegistration
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public int Layer { get; set; }
    public string? ParentModuleId { get; set; }
    public List<string>? ManagedPaths { get; set; }
    public bool IsCrossWorkModule { get; set; }
    public List<CrossWorkParticipantRegistration> Participants { get; set; } = [];
    public List<string> Dependencies { get; set; } = [];
    public string? Maintainer { get; set; }
    public string? Summary { get; set; }
    public string? Boundary { get; set; }
    public List<string>? PublicApi { get; set; }
    public List<string>? Constraints { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
}

public sealed class FeatureDefinition
{
    public List<string> Disciplines { get; set; } = [];
    public List<string> Paths { get; set; } = [];
}

public sealed class CrossWorkRegistration
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Feature { get; set; }
    public List<CrossWorkParticipantRegistration> Participants { get; set; } = [];
}

public sealed class CrossWorkParticipantRegistration
{
    public string ModuleName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string? ContractType { get; set; }
    public string? Contract { get; set; }
    public string? Deliverable { get; set; }
}

public sealed class ComputedManifest
{
    public Dictionary<string, List<string>> ModuleDependencies { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class TopologySnapshot
{
    public List<KnowledgeNode> Nodes { get; init; } = [];
    public List<TopologyRelation> Relations { get; init; } = [];
    public List<KnowledgeEdge> Edges { get; init; } = [];
    public Dictionary<string, List<string>> DepMap { get; init; } = new();
    public Dictionary<string, List<string>> RdepMap { get; init; } = new();
    public List<CrossWork> CrossWorks { get; init; } = [];
    public DateTime BuiltAt { get; init; } = DateTime.UtcNow;

    public IEnumerable<TopologyRelation> DependencyRelations =>
        Relations.Where(relation => relation.Type == TopologyRelationType.Dependency);

    public IEnumerable<TopologyRelation> ContainmentRelations =>
        Relations.Where(relation => relation.Type == TopologyRelationType.Containment);

    public IEnumerable<TopologyRelation> CollaborationRelations =>
        Relations.Where(relation => relation.Type == TopologyRelationType.Collaboration);
}
