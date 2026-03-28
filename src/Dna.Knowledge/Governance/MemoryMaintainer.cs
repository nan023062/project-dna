using Dna.Memory.Models;
using Dna.Memory.Store;
using Microsoft.Extensions.Logging;

namespace Dna.Knowledge.Governance;

/// <summary>
/// 记忆维护器 — 负责知识蒸馏、冲突检测和冗余合并
/// </summary>
internal class MemoryMaintainer
{
    private readonly MemoryStore _store;
    private readonly ILogger<MemoryMaintainer> _logger;

    public MemoryMaintainer(MemoryStore store, ILogger<MemoryMaintainer> logger)
    {
        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// 执行冲突检测，给互相矛盾的记忆打上 #conflict 标签
    /// </summary>
    public int DetectConflicts(TopologySnapshot topology)
    {
        var conflictCount = 0;
        
        var activeMemories = _store.Query(new MemoryFilter
        {
            Freshness = FreshnessFilter.FreshAndAging,
            Limit = 10000
        });

        var identityMemories = activeMemories
            .Where(m => m.Tags.Contains(WellKnownTags.Identity))
            .GroupBy(m => m.NodeId ?? "global")
            .Where(g => g.Count() > 1);

        foreach (var group in identityMemories)
        {
            var sorted = group.OrderByDescending(m => m.CreatedAt).ToList();
            for (int i = 1; i < sorted.Count; i++)
            {
                var memory = sorted[i];
                if (!memory.Tags.Contains("#conflict"))
                {
                    var updatedTags = new List<string>(memory.Tags) { "#conflict" };
                    _store.UpdateTags(memory.Id, updatedTags);
                    conflictCount++;
                    _logger.LogWarning("检测到模块 [{ModuleId}] 存在冗余的 Identity 记忆 [{MemoryId}]，已标记为 #conflict", group.Key, memory.Id);
                }
            }
        }

        return conflictCount;
    }

    /// <summary>
    /// 归档长期未使用的陈旧记忆
    /// </summary>
    public int ArchiveStaleMemories(TimeSpan staleThreshold)
    {
        var archivedCount = 0;
        var thresholdDate = DateTime.UtcNow - staleThreshold;

        var staleMemories = _store.Query(new MemoryFilter
        {
            Freshness = FreshnessFilter.IncludeStale,
            Limit = 10000
        }).Where(m => m.Freshness == FreshnessStatus.Stale);

        foreach (var memory in staleMemories)
        {
            var lastActive = memory.LastVerifiedAt ?? memory.CreatedAt;
            if (lastActive < thresholdDate)
            {
                _store.UpdateFreshness(memory.Id, FreshnessStatus.Archived);
                archivedCount++;
                _logger.LogInformation("记忆 [{Id}] 长期处于 Stale 状态，已自动归档", memory.Id);
            }
        }

        return archivedCount;
    }
}
