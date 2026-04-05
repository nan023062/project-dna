using Dna.Knowledge.TopoGraph.Models.Nodes;
using Dna.Knowledge.TopoGraph.Models.Registrations;
using Dna.Knowledge.TopoGraph.Models.Relations;
using Dna.Knowledge.TopoGraph.Models.Snapshots;
using Dna.Knowledge.TopoGraph.Models.Validation;
using TopologyRelationKindModel = Dna.Knowledge.TopoGraph.Models.Relations.TopologyRelationKind;
using TopologyRelationModel = Dna.Knowledge.TopoGraph.Models.Relations.TopologyRelation;

namespace Dna.Knowledge.TopoGraph.Contracts;

public interface ITopoGraphFacade
{
    void Initialize(string storePath);

    TopologyModelBuildResult Rebuild();
    TopologyModelSnapshot GetSnapshot();
    TopologyModelDefinition GetDefinition();
    IReadOnlyList<TopologyValidationIssue> Validate();

    void ReplaceDefinition(TopologyModelDefinition definition);

    TopologyNode? FindNode(string nodeId);
    TNode? FindNode<TNode>(string nodeId) where TNode : TopologyNode;

    IReadOnlyList<TopologyNode> GetChildren(string parentId);
    IReadOnlyList<GroupNode> GetGroups();
    IReadOnlyList<ModuleNode> GetModules();
    IReadOnlyList<DepartmentNode> GetDepartments();
    IReadOnlyList<TechnicalNode> GetTechnicalNodes();
    IReadOnlyList<TeamNode> GetTeamNodes();

    IReadOnlyList<TopologyRelationModel> GetOutgoingRelations(string nodeId, TopologyRelationKindModel? kind = null);
    IReadOnlyList<TopologyRelationModel> GetIncomingRelations(string nodeId, TopologyRelationKindModel? kind = null);
}
