using Dna.Knowledge.TopoGraph.Contracts;
using Dna.Knowledge.TopoGraph.Internal.Builders;
using Dna.Knowledge.TopoGraph.Models.Nodes;
using Dna.Knowledge.TopoGraph.Models.Registrations;
using Dna.Knowledge.TopoGraph.Models.Relations;
using Dna.Knowledge.TopoGraph.Models.Snapshots;
using Dna.Knowledge.TopoGraph.Models.Validation;
using TopologyRelationKindModel = Dna.Knowledge.TopoGraph.Models.Relations.TopologyRelationKind;
using TopologyRelationModel = Dna.Knowledge.TopoGraph.Models.Relations.TopologyRelation;

namespace Dna.Knowledge.TopoGraph;

public sealed class TopoGraphFacade : ITopoGraphFacade
{
    private readonly ITopoGraphDefinitionStore _definitionStore;
    private readonly TopologyModelBuilder _builder;
    private readonly object _lock = new();

    private TopologyModelDefinition _definition = new();
    private TopologyModelBuildResult _buildResult = new()
    {
        Snapshot = new TopologyModelSnapshot()
    };

    public TopoGraphFacade(ITopoGraphDefinitionStore definitionStore, TopologyModelBuilder builder)
    {
        _definitionStore = definitionStore;
        _builder = builder;
    }

    public void Initialize(string storePath)
    {
        lock (_lock)
        {
            _definitionStore.Initialize(storePath);
            _definitionStore.Reload();
            RebuildUnsafe();
        }
    }

    public TopologyModelBuildResult Rebuild()
    {
        lock (_lock)
            return RebuildUnsafe();
    }

    public TopologyModelSnapshot GetSnapshot()
    {
        lock (_lock)
            return _buildResult.Snapshot;
    }

    public TopologyModelDefinition GetDefinition()
    {
        lock (_lock)
            return _definition;
    }

    public IReadOnlyList<TopologyValidationIssue> Validate()
    {
        lock (_lock)
            return _buildResult.Issues;
    }

    public void ReplaceDefinition(TopologyModelDefinition definition)
    {
        lock (_lock)
        {
            _definitionStore.SaveDefinition(definition);
            _definition = definition;
            _buildResult = _builder.Build(definition);
        }
    }

    public TopologyNode? FindNode(string nodeId)
    {
        lock (_lock)
            return TryGetNode(nodeId);
    }

    public TNode? FindNode<TNode>(string nodeId) where TNode : TopologyNode
    {
        lock (_lock)
            return TryGetNode(nodeId) as TNode;
    }

    public IReadOnlyList<TopologyNode> GetChildren(string parentId)
    {
        lock (_lock)
        {
            if (!TryGetNodeMap().TryGetValue(parentId, out var parent))
                return [];

            return parent.ChildIds
                .Select(TryGetNode)
                .Where(node => node != null)
                .Cast<TopologyNode>()
                .ToList();
        }
    }

    public IReadOnlyList<GroupNode> GetGroups()
    {
        lock (_lock)
            return _buildResult.Snapshot.Groups;
    }

    public IReadOnlyList<ModuleNode> GetModules()
    {
        lock (_lock)
            return _buildResult.Snapshot.Modules;
    }

    public IReadOnlyList<DepartmentNode> GetDepartments()
    {
        lock (_lock)
            return _buildResult.Snapshot.Departments;
    }

    public IReadOnlyList<TechnicalNode> GetTechnicalNodes()
    {
        lock (_lock)
            return _buildResult.Snapshot.TechnicalNodes;
    }

    public IReadOnlyList<TeamNode> GetTeamNodes()
    {
        lock (_lock)
            return _buildResult.Snapshot.TeamNodes;
    }

    public IReadOnlyList<TopologyRelationModel> GetOutgoingRelations(string nodeId, TopologyRelationKindModel? kind = null)
    {
        lock (_lock)
            return FilterRelations(relation => relation.FromId, nodeId, kind);
    }

    public IReadOnlyList<TopologyRelationModel> GetIncomingRelations(string nodeId, TopologyRelationKindModel? kind = null)
    {
        lock (_lock)
            return FilterRelations(relation => relation.ToId, nodeId, kind);
    }

    private TopologyModelBuildResult RebuildUnsafe()
    {
        _definition = _definitionStore.LoadDefinition();
        _buildResult = _builder.Build(_definition);
        return _buildResult;
    }

    private Dictionary<string, TopologyNode> TryGetNodeMap()
        => _buildResult.Snapshot.NodeMap;

    private TopologyNode? TryGetNode(string nodeId)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
            return null;

        return TryGetNodeMap().TryGetValue(nodeId, out var node)
            ? node
            : null;
    }

    private IReadOnlyList<TopologyRelationModel> FilterRelations(
        Func<TopologyRelationModel, string> selector,
        string nodeId,
        TopologyRelationKindModel? kind)
    {
        return _buildResult.Snapshot.Relations
            .Where(relation => string.Equals(selector(relation), nodeId, StringComparison.OrdinalIgnoreCase))
            .Where(relation => !kind.HasValue || relation.Kind == kind.Value)
            .ToList();
    }
}
