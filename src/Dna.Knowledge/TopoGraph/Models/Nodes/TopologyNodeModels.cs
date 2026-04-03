using Dna.Knowledge.TopoGraph.Models.ValueObjects;

using TopologyKnowledgeSummaryModel = Dna.Knowledge.TopoGraph.Models.ValueObjects.TopologyKnowledgeSummary;
using TopologyModuleContractModel = Dna.Knowledge.TopoGraph.Models.ValueObjects.ModuleContract;
using TopologyModulePathBindingModel = Dna.Knowledge.TopoGraph.Models.ValueObjects.ModulePathBinding;

namespace Dna.Knowledge.TopoGraph.Models.Nodes;

public enum TopologyNodeKind
{
    Project,
    Department,
    Technical,
    Team
}

public abstract class TopologyNode
{
    protected TopologyNode(TopologyNodeKind kind)
    {
        Kind = kind;
    }

    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Summary { get; init; }
    public string? ParentId { get; init; }
    public List<string> ChildIds { get; } = [];
    public TopologyKnowledgeSummaryModel Knowledge { get; init; } = new();
    public TopologyNodeKind Kind { get; }
}

public abstract class GroupNode : TopologyNode
{
    protected GroupNode(TopologyNodeKind kind)
        : base(kind)
    {
    }

    public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public abstract class ModuleNode : TopologyNode
{
    protected ModuleNode(TopologyNodeKind kind)
        : base(kind)
    {
    }

    public TopologyModulePathBindingModel PathBinding { get; init; } = new();
    public string? Maintainer { get; init; }
    public int Layer { get; init; }
    public bool IsCrossWorkModule { get; init; }
    public List<TopologyCrossWorkParticipantDefinition> Participants { get; init; } = [];
    public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ProjectNode : GroupNode
{
    public ProjectNode()
        : base(TopologyNodeKind.Project)
    {
    }

    public string? Vision { get; init; }
    public string? WorkspaceRoot { get; init; }
    public string? Steward { get; init; }
    public List<string> ExcludeDirs { get; init; } = [];
}

public sealed class DepartmentNode : GroupNode
{
    public DepartmentNode()
        : base(TopologyNodeKind.Department)
    {
    }

    public string DisciplineCode { get; init; } = string.Empty;
    public string? Scope { get; init; }
    public string? Owner { get; init; }
    public string RoleId { get; init; } = "coder";
    public List<LayerDefinition> Layers { get; init; } = [];
}

public sealed class TechnicalNode : ModuleNode
{
    public TechnicalNode()
        : base(TopologyNodeKind.Technical)
    {
    }

    public TopologyModuleContractModel Contract { get; init; } = new();
    public List<string> DeclaredDependencies { get; init; } = [];
    public List<string> ComputedDependencies { get; init; } = [];
    public List<string> CapabilityTags { get; init; } = [];
}

public sealed class TeamNode : ModuleNode
{
    public TeamNode()
        : base(TopologyNodeKind.Team)
    {
    }

    public string? BusinessObjective { get; init; }
    public List<string> TechnicalDependencies { get; init; } = [];
    public List<string> Deliverables { get; init; } = [];
    public List<string> CollaborationIds { get; init; } = [];
}
