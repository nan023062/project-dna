using Dna.Memory.Models;
using Dna.Memory.Store;
using Microsoft.Extensions.Logging;

namespace Dna.Knowledge.Governance;

/// <summary>
/// 记忆鲜活度检查器 — 基于时间的自动降级（Server 不访问项目源码，不做路径变更检测）。
/// </summary>
internal class FreshnessChecker
{
    private readonly MemoryStore _store;
    private readonly ILogger<FreshnessChecker> _logger;

    public FreshnessChecker(MemoryStore store, ILogger<FreshnessChecker> logger)
    {
        _store = store;
        _logger = logger;
    }

    public int CheckAll(TopologySnapshot topology)
    {
        return _store.DecayStaleMemories();
    }
}
