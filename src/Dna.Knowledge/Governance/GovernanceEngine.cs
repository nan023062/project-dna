using Dna.Knowledge.Governance;
using Dna.Memory.Store;
using Microsoft.Extensions.Logging;

namespace Dna.Knowledge;

/// <summary>
/// 治理引擎 — 架构健康检查、鲜活度扫描、冲突检测、归档。
/// </summary>
public sealed class GovernanceEngine : IGovernanceEngine
{
    private readonly IGraphEngine _graphEngine;
    private readonly FreshnessChecker _freshnessChecker;
    private readonly MemoryMaintainer _memoryMaintainer;

    public GovernanceEngine(MemoryStore memoryStore, ITopoGraphStore topoGraphStore, IGraphEngine graphEngine, ILoggerFactory loggerFactory)
    {
        _graphEngine = graphEngine;
        _freshnessChecker = new FreshnessChecker(memoryStore, loggerFactory.CreateLogger<FreshnessChecker>());
        _memoryMaintainer = new MemoryMaintainer(memoryStore, topoGraphStore, loggerFactory.CreateLogger<MemoryMaintainer>());
    }

    public GovernanceReport ValidateArchitecture()
    {
        _graphEngine.BuildTopology();
        return _graphEngine.ValidateArchitecture();
    }

    public int CheckFreshness() => _freshnessChecker.CheckAll();

    public int DetectMemoryConflicts() => _memoryMaintainer.DetectConflicts();

    public int ArchiveStaleMemories(TimeSpan staleThreshold)
        => _memoryMaintainer.ArchiveStaleMemories(staleThreshold);

    public Task<KnowledgeCondenseResult> CondenseNodeKnowledgeAsync(string nodeIdOrName, int maxSourceMemories = 200)
        => _memoryMaintainer.CondenseNodeKnowledgeAsync(nodeIdOrName, maxSourceMemories);

    public Task<List<KnowledgeCondenseResult>> CondenseAllNodesAsync(int maxSourceMemories = 200)
        => _memoryMaintainer.CondenseAllNodesAsync(maxSourceMemories);
}
