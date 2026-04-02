using Dna.Knowledge.TopoGraph.Models.Nodes;
using Dna.Knowledge.TopoGraph.Models.Relations;
using TopologyRelationKindModel = Dna.Knowledge.TopoGraph.Models.Relations.TopologyRelationKind;
using TopologyRelationModel = Dna.Knowledge.TopoGraph.Models.Relations.TopologyRelation;

namespace Dna.Knowledge.TopoGraph.Models.Snapshots;

public sealed class TopologyModelSnapshot
{
    public ProjectNode? Project { get; init; }
    public List<TopologyNode> Nodes { get; init; } = [];
    public List<TopologyRelationModel> Relations { get; init; } = [];
    public Dictionary<string, TopologyNode> NodeMap { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public List<GroupNode> Groups => Nodes.OfType<GroupNode>().ToList();
    public List<ModuleNode> Modules => Nodes.OfType<ModuleNode>().ToList();
    public List<DepartmentNode> Departments => Nodes.OfType<DepartmentNode>().ToList();
    public List<TechnicalNode> TechnicalNodes => Nodes.OfType<TechnicalNode>().ToList();
    public List<TeamNode> TeamNodes => Nodes.OfType<TeamNode>().ToList();

    public List<TopologyRelationModel> Containments =>
        Relations.Where(item => item.Kind == TopologyRelationKindModel.Containment).ToList();

    public List<TopologyRelationModel> Dependencies =>
        Relations.Where(item => item.Kind == TopologyRelationKindModel.Dependency).ToList();

    public List<TopologyRelationModel> Collaborations =>
        Relations.Where(item => item.Kind == TopologyRelationKindModel.Collaboration).ToList();
}
