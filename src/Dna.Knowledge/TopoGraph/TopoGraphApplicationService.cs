using Dna.Knowledge.TopoGraph.Contracts;
using Dna.Knowledge.TopoGraph.Internal.Builders;
using Dna.Knowledge.Workspace.Models;
using Microsoft.Extensions.Logging;

namespace Dna.Knowledge;

public sealed partial class TopoGraphApplicationService : ITopoGraphApplicationService
{
    private readonly ITopoGraphStore _store;
    private readonly ITopoGraphFacade _facade;
    private readonly ITopoGraphContextProvider? _contextProvider;
    private readonly ILogger _logger;
    private readonly object _lock = new();

    private IProjectAdapter? _adapter;
    private TopologySnapshot _topology = new();
    private bool _topologyBuilt;

    public TopoGraphApplicationService(
        ITopoGraphStore store,
        ITopoGraphFacade facade,
        ITopoGraphContextProvider? contextProvider,
        ILogger logger)
    {
        _store = store;
        _facade = facade;
        _contextProvider = contextProvider;
        _logger = logger;
    }

    public void SetAdapter(IProjectAdapter adapter) => _adapter = adapter;

    public TopologySnapshot BuildTopology()
    {
        lock (_lock)
        {
            _topology = BuildTopologyLocked();
            return _topology;
        }
    }

    public TopologySnapshot GetTopology()
    {
        lock (_lock)
        {
            EnsureTopologyReadyLocked();
            return _topology;
        }
    }

    public TopologyManagementSnapshot GetManagementSnapshot()
    {
        lock (_lock)
            return BuildManagementSnapshot(_facade.GetSnapshot());
    }

    public TopologyModuleKnowledgeView? GetModuleKnowledge(string nodeIdOrName)
    {
        lock (_lock)
        {
            var node = FindModuleCore(nodeIdOrName);
            return node == null ? null : ToModuleKnowledgeView(node);
        }
    }

    public IReadOnlyList<TopologyModuleKnowledgeView> ListModuleKnowledge()
    {
        lock (_lock)
        {
            EnsureTopologyReadyLocked();
            return _topology.Nodes
                .OrderBy(node => node.Discipline ?? "root", StringComparer.OrdinalIgnoreCase)
                .ThenBy(node => node.Layer)
                .ThenBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
                .Select(ToModuleKnowledgeView)
                .ToList();
        }
    }

    public TopologyModuleKnowledgeView SaveModuleKnowledge(TopologyModuleKnowledgeUpsertCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        lock (_lock)
        {
            var node = FindModuleCore(command.NodeIdOrName)
                ?? throw new InvalidOperationException(string.Format(TopoGraphConstants.Context.MissingModuleTemplate, command.NodeIdOrName));

            var updatedKnowledge = CloneNodeKnowledge(command.Knowledge);
            _store.UpsertNodeKnowledge(node.Id, updatedKnowledge);
            InvalidateTopologyCacheLocked();
            EnsureTopologyReadyLocked();

            var refreshed = FindModuleCore(node.Id)
                ?? throw new InvalidOperationException(string.Format(TopoGraphConstants.Context.MissingModuleTemplate, node.Id));

            return ToModuleKnowledgeView(refreshed);
        }
    }

    public TopologyModuleRelationsView? GetModuleRelations(string nodeIdOrName)
    {
        lock (_lock)
        {
            var node = FindModuleCore(nodeIdOrName);
            if (node == null)
                return null;

            var nodeNames = _topology.Nodes.ToDictionary(item => item.Id, item => item.Name, StringComparer.OrdinalIgnoreCase);
            var outgoing = _topology.Relations
                .Where(relation => string.Equals(relation.FromId, node.Id, StringComparison.OrdinalIgnoreCase))
                .Select(relation => ToModuleRelationView(relation, nodeNames))
                .ToList();
            var incoming = _topology.Relations
                .Where(relation => string.Equals(relation.ToId, node.Id, StringComparison.OrdinalIgnoreCase))
                .Select(relation => ToModuleRelationView(relation, nodeNames))
                .ToList();

            return new TopologyModuleRelationsView
            {
                NodeId = node.Id,
                Name = node.Name,
                Outgoing = outgoing,
                Incoming = incoming
            };
        }
    }

    public ExecutionPlan GetExecutionPlan(List<string> moduleNames)
    {
        lock (_lock)
        {
            EnsureTopologyReadyLocked();
            return TopologyBuilder.GetExecutionPlan(_topology, moduleNames);
        }
    }

    public KnowledgeNode? FindModule(string nameOrPath)
    {
        lock (_lock)
            return FindModuleCore(nameOrPath);
    }

    public List<KnowledgeNode> GetAllModules()
    {
        lock (_lock)
        {
            EnsureTopologyReadyLocked();
            return _topology.Nodes;
        }
    }

    public List<KnowledgeNode> GetModulesByDiscipline(string disciplineId)
    {
        lock (_lock)
        {
            EnsureTopologyReadyLocked();
            return _topology.Nodes
                .Where(node => string.Equals(node.Discipline, disciplineId, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }

    public ModuleContext GetModuleContext(string targetModule, string? currentModule, List<string>? activeModules = null)
    {
        lock (_lock)
        {
            EnsureTopologyReadyLocked();
            return ContextFilter.BuildContext(targetModule, currentModule, _topology, _contextProvider, _adapter, activeModules);
        }
    }

    public GovernanceReport ValidateArchitecture()
    {
        lock (_lock)
        {
            EnsureTopologyReadyLocked();
            return GovernanceAnalyzer.Analyze(_topology, _adapter);
        }
    }

    public List<CrossWork> GetCrossWorks()
    {
        lock (_lock)
        {
            EnsureTopologyReadyLocked();
            return _topology.CrossWorks;
        }
    }

    public List<CrossWork> GetCrossWorksForModule(string moduleName)
    {
        lock (_lock)
        {
            EnsureTopologyReadyLocked();
            return _topology.CrossWorks
                .Where(crossWork => crossWork.Participants.Any(participant =>
                    string.Equals(participant.ModuleName, moduleName, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }
    }

    public void RegisterModule(string discipline, TopologyModuleDefinition module)
    {
        lock (_lock)
            RegisterModuleCore(discipline, module);
    }

    public bool UnregisterModule(string name)
    {
        lock (_lock)
            return UnregisterModuleCore(name);
    }

    public void SaveCrossWork(TopologyCrossWorkDefinition crossWork)
    {
        lock (_lock)
            SaveCrossWorkCore(crossWork);
    }

    public bool RemoveCrossWork(string crossWorkId)
    {
        lock (_lock)
            return RemoveCrossWorkCore(crossWorkId);
    }

    public void UpsertDiscipline(string disciplineId, string? displayName, string roleId, List<LayerDefinition> layers)
    {
        lock (_lock)
            UpsertDisciplineCore(disciplineId, displayName, roleId, layers);
    }

    public bool RemoveDiscipline(string disciplineId)
    {
        lock (_lock)
            return RemoveDisciplineCore(disciplineId);
    }

    public string? GetDisciplineRoleId(string moduleName)
    {
        lock (_lock)
        {
            var node = FindModuleCore(moduleName);
            if (node?.Discipline == null)
                return null;

            var discipline = GetManagementSnapshot().Disciplines.FirstOrDefault(item =>
                string.Equals(item.Id, node.Discipline, StringComparison.OrdinalIgnoreCase));
            return discipline?.RoleId;
        }
    }

    public WorkspaceTopologyContext GetWorkspaceContext()
    {
        var snapshot = GetManagementSnapshot();
        var modules = snapshot.Modules
            .Select(module => new WorkspaceModuleRegistration
            {
                Id = module.Id,
                Name = module.Name,
                Discipline = module.Discipline,
                Layer = module.Layer,
                IsCrossWorkModule = module.IsCrossWorkModule,
                Path = module.Path,
                ManagedPaths = module.ManagedPaths?.Where(path => !string.IsNullOrWhiteSpace(path)).ToList() ?? []
            })
            .ToList();

        return new WorkspaceTopologyContext
        {
            ExcludeDirs = snapshot.ExcludeDirs,
            Modules = modules
        };
    }

    public void ReloadManifests()
    {
        lock (_lock)
        {
            _facade.Rebuild();
            _store.Reload();
            InvalidateTopologyCacheLocked();
        }
    }

    public void Initialize(string storePath)
    {
        _store.Initialize(storePath);
        _facade.Initialize(storePath);

        lock (_lock)
            InvalidateTopologyCacheLocked();
    }

    private TopologySnapshot BuildTopologyLocked()
    {
        _facade.Rebuild();
        _store.Reload();
        var topology = BuildCompatibilityTopology();

        if (_adapter != null)
        {
            var hasComputedUpdates = false;
            foreach (var node in topology.Nodes.Where(node => node.Type is NodeType.Technical or NodeType.Team))
            {
                var computed = _adapter.ComputeDependencies(node, topology.Nodes);
                if (computed.Count == 0)
                    continue;

                _store.UpdateComputedDependencies(node.Name, computed);
                hasComputedUpdates = true;
            }

            if (hasComputedUpdates)
            {
                _store.Reload();
                topology = BuildCompatibilityTopology();
            }
        }

        _logger.LogInformation(
            TopoGraphConstants.Logging.TopologySummary,
            topology.Nodes.Count,
            topology.Relations.Count,
            topology.Edges.Count,
            topology.CrossWorks.Count);

        _topologyBuilt = true;
        return topology;
    }

    private void EnsureTopologyReadyLocked()
    {
        if (_topologyBuilt)
            return;

        _topology = BuildTopologyLocked();
    }

    private void InvalidateTopologyCacheLocked()
    {
        _topology = new TopologySnapshot();
        _topologyBuilt = false;
    }

    private KnowledgeNode? FindModuleCore(string nameOrPath)
    {
        EnsureTopologyReadyLocked();
        return _topology.Nodes.FirstOrDefault(node =>
            string.Equals(node.Name, nameOrPath, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(node.RelativePath, NormalizePath(nameOrPath), StringComparison.OrdinalIgnoreCase) ||
            string.Equals(node.Id, nameOrPath, StringComparison.OrdinalIgnoreCase));
    }
}
