using Dna.Knowledge.Governance;
using Dna.Memory.Store;
using Microsoft.Extensions.Logging;

namespace Dna.Knowledge;

/// <summary>
/// 治理引擎 — 架构健康检查、鲜活度扫描、冲突检测、归档。
/// </summary>
public sealed class GovernanceEngine : IGovernanceEngine
{
    private readonly GraphEngine _graphEngine;
    private readonly FreshnessChecker _freshnessChecker;
    private readonly MemoryMaintainer _memoryMaintainer;

    internal GovernanceEngine(DnaServiceHolder holder, GraphEngine graphEngine, ILoggerFactory loggerFactory)
    {
        _graphEngine = graphEngine;
        _freshnessChecker = new FreshnessChecker(holder.Store, loggerFactory.CreateLogger<FreshnessChecker>());
        _memoryMaintainer = new MemoryMaintainer(holder.Store, loggerFactory.CreateLogger<MemoryMaintainer>());
    }

    public GovernanceReport ValidateArchitecture()
        => _graphEngine.ValidateArchitectureInternal();

    public int CheckFreshness()
    {
        var topology = _graphEngine.GetTopology();
        return _freshnessChecker.CheckAll(topology);
    }

    public int DetectMemoryConflicts()
    {
        var topology = _graphEngine.GetTopology();
        return _memoryMaintainer.DetectConflicts(topology);
    }

    public int ArchiveStaleMemories(TimeSpan staleThreshold)
        => _memoryMaintainer.ArchiveStaleMemories(staleThreshold);

    public Task<KnowledgeCondenseResult> CondenseNodeKnowledgeAsync(string nodeIdOrName, int maxSourceMemories = 200)
    {
        var topology = _graphEngine.GetTopology();
        return _memoryMaintainer.CondenseNodeKnowledgeAsync(topology, nodeIdOrName, maxSourceMemories);
    }

    public Task<List<KnowledgeCondenseResult>> CondenseAllNodesAsync(int maxSourceMemories = 200)
    {
        var topology = _graphEngine.GetTopology();
        return _memoryMaintainer.CondenseAllNodesAsync(topology, maxSourceMemories);
    }
}
