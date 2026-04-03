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
            _facade.Rebuild();
            _store.Reload();
            _topology = BuildCompatibilityTopology();

            if (_adapter != null)
            {
                var hasComputedUpdates = false;
                foreach (var node in _topology.Nodes.Where(node => node.Type is NodeType.Technical or NodeType.Team))
                {
                    var computed = _adapter.ComputeDependencies(node, _topology.Nodes);
                    if (computed.Count == 0)
                        continue;

                    _store.UpdateComputedDependencies(node.Name, computed);
                    hasComputedUpdates = true;
                }

                if (hasComputedUpdates)
                {
                    _store.Reload();
                    _topology = BuildCompatibilityTopology();
                }
            }

            _logger.LogInformation(
                TopoGraphConstants.Logging.TopologySummary,
                _topology.Nodes.Count,
                _topology.Relations.Count,
                _topology.Edges.Count,
                _topology.CrossWorks.Count);

            return _topology;
        }
    }

    public TopologySnapshot GetTopology()
    {
        lock (_lock)
            return _topology;
    }

    public TopologyManagementSnapshot GetManagementSnapshot()
    {
        lock (_lock)
            return BuildManagementSnapshot(_facade.GetSnapshot());
    }

    public ExecutionPlan GetExecutionPlan(List<string> moduleNames)
    {
        lock (_lock)
            return TopologyBuilder.GetExecutionPlan(_topology, moduleNames);
    }

    public KnowledgeNode? FindModule(string nameOrPath)
    {
        lock (_lock)
        {
            return _topology.Nodes.FirstOrDefault(node =>
                string.Equals(node.Name, nameOrPath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(node.RelativePath, NormalizePath(nameOrPath), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(node.Id, nameOrPath, StringComparison.OrdinalIgnoreCase));
        }
    }

    public List<KnowledgeNode> GetAllModules()
    {
        lock (_lock)
            return _topology.Nodes;
    }

    public List<KnowledgeNode> GetModulesByDiscipline(string disciplineId)
    {
        lock (_lock)
        {
            return _topology.Nodes
                .Where(node => string.Equals(node.Discipline, disciplineId, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }

    public ModuleContext GetModuleContext(string targetModule, string? currentModule, List<string>? activeModules = null)
    {
        lock (_lock)
            return ContextFilter.BuildContext(targetModule, currentModule, _topology, _contextProvider, _adapter, activeModules);
    }

    public GovernanceReport ValidateArchitecture()
    {
        lock (_lock)
            return GovernanceAnalyzer.Analyze(_topology, _adapter);
    }

    public List<CrossWork> GetCrossWorks()
    {
        lock (_lock)
            return _topology.CrossWorks;
    }

    public List<CrossWork> GetCrossWorksForModule(string moduleName)
    {
        lock (_lock)
        {
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
            var node = _topology.Nodes.FirstOrDefault(item =>
                string.Equals(item.Name, moduleName, StringComparison.OrdinalIgnoreCase));
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
        }
    }

    public void Initialize(string storePath)
    {
        _store.Initialize(storePath);
        _facade.Initialize(storePath);
    }
}
