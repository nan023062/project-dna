using Dna.Core.Framework;
using Dna.Knowledge.Models;
using Dna.Knowledge.Project;
using Dna.Knowledge.Project.Models;
using Dna.Memory.Store;
using Microsoft.Extensions.Logging;

namespace Dna.Knowledge;

/// <summary>
/// 图谱引擎 — 拓扑构建、模块/部门查询、上下文、注册、工程扫描。
/// </summary>
public sealed class GraphEngine : IGraphEngine, IDnaService, IDisposable
{
    private readonly MemoryStore _store;
    private readonly ProjectTreeCache _treeCache;
    private readonly ILogger<GraphEngine> _logger;
    private IProjectAdapter? _adapter;
    private TopologySnapshot _topology = new();
    private readonly object _lock = new();

    public string ServiceName => "GraphEngine";

    internal GraphEngine(DnaServiceHolder holder, ILogger<GraphEngine> logger)
    {
        _store = holder.Store;
        _treeCache = holder.TreeCache;
        _logger = logger;
    }

    internal void SetAdapter(IProjectAdapter adapter) => _adapter = adapter;

    // ═══════════════════════════════════════════
    //  拓扑
    // ═══════════════════════════════════════════

    public TopologySnapshot BuildTopology()
    {
        lock (_lock)
        {
            _topology = TopologyBuilder.Build(_store);

            if (_adapter != null)
            {
                var hasUpdates = false;
                foreach (var node in _topology.Nodes)
                {
                    var computed = _adapter.ComputeDependencies(node, _topology.Nodes);
                    if (computed.Count > 0)
                    {
                        _store.UpdateComputedDependencies(node.Name, computed);
                        hasUpdates = true;
                    }
                }

                if (hasUpdates)
                {
                    _topology = TopologyBuilder.Build(_store);
                }
            }

            _logger.LogInformation("[GraphEngine] Topology: {Nodes} nodes, {Edges} edges, {CW} crossworks",
                _topology.Nodes.Count, _topology.Edges.Count, _topology.CrossWorks.Count);
            return _topology;
        }
    }

    public TopologySnapshot GetTopology()
    {
        lock (_lock) return _topology;
    }

    public ExecutionPlan GetExecutionPlan(List<string> moduleNames)
    {
        lock (_lock) return TopologyBuilder.GetExecutionPlan(_topology, moduleNames);
    }

    // ═══════════════════════════════════════════
    //  模块查询
    // ═══════════════════════════════════════════

    public KnowledgeNode? FindModule(string nameOrPath)
    {
        lock (_lock)
        {
            return _topology.Nodes.FirstOrDefault(n =>
                string.Equals(n.Name, nameOrPath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(n.RelativePath, nameOrPath.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase));
        }
    }

    public List<KnowledgeNode> GetAllModules()
    {
        lock (_lock) return _topology.Nodes;
    }

    public List<KnowledgeNode> GetModulesByDiscipline(string disciplineId)
    {
        lock (_lock)
        {
            return _topology.Nodes
                .Where(n => string.Equals(n.Discipline, disciplineId, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }

    public string? GetDisciplineRoleId(string moduleName)
    {
        lock (_lock)
        {
            var node = _topology.Nodes.FirstOrDefault(n =>
                string.Equals(n.Name, moduleName, StringComparison.OrdinalIgnoreCase));
            if (node?.Discipline == null) return null;
            var arch = _store.GetArchitecture();
            return arch.Disciplines.TryGetValue(node.Discipline, out var def) ? def.RoleId : null;
        }
    }

    // ═══════════════════════════════════════════
    //  上下文
    // ═══════════════════════════════════════════

    public ModuleContext GetModuleContext(string targetModule, string? currentModule, List<string>? activeModules = null)
    {
        lock (_lock)
        {
            return ContextFilter.BuildContext(targetModule, currentModule, _topology, _store, _adapter, activeModules);
        }
    }

    // ═══════════════════════════════════════════
    //  CrossWork 查询
    // ═══════════════════════════════════════════

    public List<CrossWork> GetCrossWorks()
    {
        lock (_lock) return _topology.CrossWorks;
    }

    public List<CrossWork> GetCrossWorksForModule(string moduleName)
    {
        lock (_lock)
        {
            return _topology.CrossWorks
                .Where(cw => cw.Participants.Any(p =>
                    p.ModuleName.Equals(moduleName, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }
    }

    // ═══════════════════════════════════════════
    //  注册 / 清单
    // ═══════════════════════════════════════════

    public void RegisterModule(string discipline, ModuleRegistration module) => _store.RegisterModule(discipline, module);
    public bool UnregisterModule(string name) => _store.UnregisterModule(name);
    public void SaveCrossWork(CrossWorkRegistration crossWork) => _store.SaveCrossWork(crossWork);
    public bool RemoveCrossWork(string crossWorkId) => _store.RemoveCrossWork(crossWorkId);

    public void UpsertDiscipline(string disciplineId, string? displayName, string roleId, List<LayerDefinition> layers)
        => _store.UpsertDiscipline(disciplineId, displayName, roleId, layers);

    public bool RemoveDiscipline(string disciplineId) => _store.RemoveDiscipline(disciplineId);

    public ArchitectureManifest GetArchitecture() => _store.GetArchitecture();
    public ModulesManifest GetModulesManifest() => _store.GetModulesManifest();
    public void ReplaceModulesManifest(ModulesManifest manifest) => _store.ReplaceModulesManifest(manifest);
    public void ReloadManifests() => _store.Reload();

    // ═══════════════════════════════════════════
    //  初始化
    // ═══════════════════════════════════════════

    public void Initialize(string storePath) => _store.Initialize(storePath);

    // ═══════════════════════════════════════════
    //  治理（仅对外暴露 — GovernanceEngine 也调用同一静态方法）
    // ═══════════════════════════════════════════

    internal GovernanceReport ValidateArchitectureInternal()
    {
        lock (_lock) return GovernanceAnalyzer.Analyze(_topology, _adapter);
    }

    internal IProjectAdapter? Adapter => _adapter;

    public void Dispose()
    {
        // DnaServiceHolder owns the disposable resources
        GC.SuppressFinalize(this);
    }
}
