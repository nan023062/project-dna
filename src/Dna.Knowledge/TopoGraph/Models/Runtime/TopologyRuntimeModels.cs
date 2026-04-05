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
    public string? IdentityMemoryId { get; set; }
    public string? UpgradeTrailMemoryId { get; set; }
    public List<string> MemoryIds { get; set; } = [];
}

public sealed class TopologyModuleKnowledgeView
{
    public string NodeId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public NodeType Type { get; init; } = NodeType.Technical;
    public string? Discipline { get; init; }
    public string? ParentId { get; init; }
    public int Layer { get; init; }
    public string? RelativePath { get; init; }
    public List<string> ManagedPaths { get; init; } = [];
    public string? Maintainer { get; init; }
    public string? Summary { get; init; }
    public string? Boundary { get; init; }
    public List<string> PublicApi { get; init; } = [];
    public List<string> Constraints { get; init; } = [];
    public List<string> DeclaredDependencies { get; init; } = [];
    public List<string> ComputedDependencies { get; init; } = [];
    public bool IsCrossWorkModule { get; init; }
    public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public NodeKnowledge Knowledge { get; init; } = new();
}

public sealed class TopologyModuleKnowledgeUpsertCommand
{
    public string NodeIdOrName { get; set; } = string.Empty;
    public NodeKnowledge Knowledge { get; set; } = new();
}

public sealed class TopologyModuleRelationsView
{
    public string NodeId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public List<TopologyModuleRelationView> Outgoing { get; init; } = [];
    public List<TopologyModuleRelationView> Incoming { get; init; } = [];
}

public sealed class TopologyWorkbenchSnapshot
{
    public TopologyWorkbenchProjectView Project { get; init; } = new();
    public List<TopologyWorkbenchModuleView> Modules { get; init; } = [];
    public List<TopologyWorkbenchRelationEdgeView> Edges { get; init; } = [];
    public List<TopologyWorkbenchRelationEdgeView> RelationEdges { get; init; } = [];
    public List<TopologyWorkbenchRelationEdgeView> ContainmentEdges { get; init; } = [];
    public List<TopologyWorkbenchRelationEdgeView> CollaborationEdges { get; init; } = [];
    public List<TopologyWorkbenchCrossWorkView> CrossWorks { get; init; } = [];
    public List<TopologyWorkbenchDisciplineView> Disciplines { get; init; } = [];
    public Dictionary<string, List<string>> DepMap { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<string>> RdepMap { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public string Summary { get; init; } = string.Empty;
    public DateTime ScannedAt { get; init; } = DateTime.UtcNow;
}

public sealed class TopologyWorkbenchProjectView
{
    public string Id { get; init; } = "project";
    public string Name { get; init; } = "Project";
    public string Type { get; init; } = "Project";
    public string TypeName { get; init; } = "Project";
    public string TypeLabel { get; init; } = "Project";
    public string FileAuthority { get; init; } = "govern";
    public string? Summary { get; init; }
    public List<string> ManagedPathScopes { get; init; } = [];
}

public sealed class TopologyWorkbenchModuleView
{
    public string Name { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string NodeId { get; init; } = string.Empty;
    public string? RelativePath { get; init; }
    public int Layer { get; init; }
    public int StructureDepth { get; init; }
    public int ArchitectureLayerScore { get; init; }
    public string Discipline { get; init; } = "root";
    public string DisciplineDisplayName { get; init; } = "root";
    public string Type { get; init; } = "Technical";
    public string TypeName { get; init; } = "Technical";
    public string TypeLabel { get; init; } = "Technical";
    public string? Summary { get; init; }
    public List<string> Keywords { get; init; } = [];
    public List<string> Dependencies { get; init; } = [];
    public List<string> ComputedDependencies { get; init; } = [];
    public string? Maintainer { get; init; }
    public string? Boundary { get; init; }
    public string? Contract { get; init; }
    public List<string> PublicApi { get; init; } = [];
    public List<string> Constraints { get; init; } = [];
    public List<string> Workflow { get; init; } = [];
    public List<string> Rules { get; init; } = [];
    public List<string> Prohibitions { get; init; } = [];
    public string FileAuthority { get; init; } = "govern";
    public List<string> ManagedPathScopes { get; init; } = [];
    public Dictionary<string, string>? Metadata { get; init; }
    public string? ParentId { get; init; }
    public string? ParentModuleId { get; init; }
    public List<string> ChildIds { get; init; } = [];
    public bool IsCrossWorkModule { get; init; }
    public bool CanEdit { get; init; }
}

public sealed class TopologyWorkbenchRelationEdgeView
{
    public string From { get; init; } = string.Empty;
    public string To { get; init; } = string.Empty;
    public string Relation { get; init; } = "dependency";
    public string? Kind { get; init; }
    public bool IsComputed { get; init; }
}

public sealed class TopologyWorkbenchCrossWorkView
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? Feature { get; init; }
    public List<TopologyWorkbenchCrossWorkParticipantView> Participants { get; init; } = [];
}

public sealed class TopologyWorkbenchCrossWorkParticipantView
{
    public string ModuleName { get; init; } = string.Empty;
    public string? ModuleId { get; init; }
    public string Role { get; init; } = string.Empty;
    public string? Contract { get; init; }
    public string? ContractType { get; init; }
    public string? Deliverable { get; init; }
}

public sealed class TopologyWorkbenchDisciplineView
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string? RoleId { get; init; }
    public int ModuleCount { get; init; }
    public List<string> Modules { get; init; } = [];
    public int CrossWorkCount { get; init; }
}

public sealed class McdpProjectGraph
{
    public string ProtocolVersion { get; init; } = "1.0";
    public string? ProjectRoot { get; init; }
    public string ProjectName { get; init; } = "Project";
    public List<McdpModuleView> Modules { get; init; } = [];
}

public sealed class McdpModuleView
{
    public string Uid { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int LayerScore { get; init; }
    public string Type { get; init; } = "Technical";
    public McdpDnaView Dna { get; init; } = new();
    public McdpRelationshipsView Relationships { get; init; } = new();
}

public sealed class McdpDnaView
{
    public string? Summary { get; init; }
    public List<string> Keywords { get; init; } = [];
    public List<string> Contract { get; init; } = [];
}

public sealed class McdpRelationshipsView
{
    public string? Parent { get; init; }
    public List<string> Children { get; init; } = [];
    public List<McdpDependencyView> Dependencies { get; init; } = [];
}

public sealed class McdpDependencyView
{
    public string Target { get; init; } = string.Empty;
    public string Type { get; init; } = "Association";
    public bool IsComputed { get; init; }
}

public sealed class TopologyModuleRelationView
{
    public string FromId { get; init; } = string.Empty;
    public string FromName { get; init; } = string.Empty;
    public string ToId { get; init; } = string.Empty;
    public string ToName { get; init; } = string.Empty;
    public TopologyRelationType Type { get; init; } = TopologyRelationType.Dependency;
    public bool IsComputed { get; init; }
    public string? Label { get; init; }
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
