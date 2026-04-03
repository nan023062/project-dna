using Dna.Knowledge.TopoGraph.Models.ValueObjects;

using TopologyKnowledgeSummaryModel = Dna.Knowledge.TopoGraph.Models.ValueObjects.TopologyKnowledgeSummary;
using TopologyModuleContractModel = Dna.Knowledge.TopoGraph.Models.ValueObjects.ModuleContract;
using TopologyModulePathBindingModel = Dna.Knowledge.TopoGraph.Models.ValueObjects.ModulePathBinding;

namespace Dna.Knowledge.TopoGraph.Models.Registrations;

public sealed class TopologyModelDefinition
{
    public ProjectNodeRegistration? Project { get; init; }
    public List<DepartmentNodeRegistration> Departments { get; init; } = [];
    public List<TechnicalNodeRegistration> TechnicalNodes { get; init; } = [];
    public List<TeamNodeRegistration> TeamNodes { get; init; } = [];
    public List<CollaborationRegistration> Collaborations { get; init; } = [];
}

public abstract class TopologyNodeRegistration
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Summary { get; init; }
    public string? ParentId { get; init; }
    public TopologyKnowledgeSummaryModel Knowledge { get; init; } = new();
}

public abstract class GroupNodeRegistration : TopologyNodeRegistration
{
    public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public abstract class ModuleNodeRegistration : TopologyNodeRegistration
{
    public TopologyModulePathBindingModel PathBinding { get; init; } = new();
    public string? Maintainer { get; init; }
    public int Layer { get; init; }
    public bool IsCrossWorkModule { get; init; }
    public List<TopologyCrossWorkParticipantDefinition> Participants { get; init; } = [];
    public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ProjectNodeRegistration : GroupNodeRegistration
{
    public string? Vision { get; init; }
    public string? WorkspaceRoot { get; init; }
    public string? Steward { get; init; }
    public List<string> ExcludeDirs { get; init; } = [];
}

public sealed class DepartmentNodeRegistration : GroupNodeRegistration
{
    public string DisciplineCode { get; init; } = string.Empty;
    public string? Scope { get; init; }
    public string? Owner { get; init; }
    public string RoleId { get; init; } = "coder";
    public List<LayerDefinition> Layers { get; init; } = [];
}

public sealed class TechnicalNodeRegistration : ModuleNodeRegistration
{
    public TopologyModuleContractModel Contract { get; init; } = new();
    public List<string> DeclaredDependencies { get; init; } = [];
    public List<string> ComputedDependencies { get; init; } = [];
    public List<string> CapabilityTags { get; init; } = [];
}

public sealed class TeamNodeRegistration : ModuleNodeRegistration
{
    public string? BusinessObjective { get; init; }
    public List<string> TechnicalDependencies { get; init; } = [];
    public List<string> Deliverables { get; init; } = [];
    public List<string> CollaborationIds { get; init; } = [];
}

public sealed class CollaborationRegistration
{
    public string FromId { get; init; } = string.Empty;
    public string ToId { get; init; } = string.Empty;
    public string? Label { get; init; }
}
