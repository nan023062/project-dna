using Dna.Memory.Models;
using Dna.Memory.Store;
using Microsoft.Extensions.Logging;

namespace Dna.Memory.Services;

/// <summary>
/// 记忆读取服务 — 结构化查询、约束链、业务系统/职能汇总。
/// 语义检索见 MemoryRecallEngine。
/// </summary>
internal class MemoryReader
{
    private readonly MemoryStore _store;
    private readonly ILogger<MemoryReader> _logger;

    public MemoryReader(MemoryStore store, ILogger<MemoryReader> logger)
    {
        _store = store;
        _logger = logger;
    }

    public MemoryEntry? GetById(string memoryId) => _store.GetById(memoryId);

    public List<MemoryEntry> Query(MemoryFilter filter) => _store.Query(filter);

    /// <summary>获取完整约束链（从 L0 到当前条目）</summary>
    public List<MemoryEntry> GetConstraintChain(string memoryId) => _store.GetConstraintChain(memoryId);

    /// <summary>
    /// 获取业务系统的全职能知识汇总。
    /// 按职能域分组，并附带跨职能协议。
    /// </summary>
    public FeatureKnowledgeSummary GetFeatureSummary(string featureId)
    {
        var allEntries = _store.Query(new MemoryFilter
        {
            Features = [featureId],
            Freshness = FreshnessFilter.FreshAndAging,
            Limit = 200
        });

        var byDiscipline = new Dictionary<string, List<MemoryEntry>>();
        var crossDiscipline = new List<MemoryEntry>();

        foreach (var entry in allEntries)
        {
            if (entry.Layer == KnowledgeLayer.CrossDiscipline)
            {
                crossDiscipline.Add(entry);
                continue;
            }

            foreach (var disc in entry.Disciplines)
            {
                if (!byDiscipline.TryGetValue(disc, out var list))
                {
                    list = [];
                    byDiscipline[disc] = list;
                }
                list.Add(entry);
            }
        }

        return new FeatureKnowledgeSummary
        {
            FeatureId = featureId,
            ByDiscipline = byDiscipline,
            CrossDiscipline = crossDiscipline,
            TotalCount = allEntries.Count
        };
    }

    /// <summary>获取职能知识汇总（按层级分组）</summary>
    public DisciplineKnowledgeSummary GetDisciplineSummary(string disciplineId)
    {
        var allEntries = _store.Query(new MemoryFilter
        {
            Disciplines = [disciplineId],
            Freshness = FreshnessFilter.FreshAndAging,
            Limit = 200
        });

        var byLayer = new Dictionary<KnowledgeLayer, List<MemoryEntry>>();
        foreach (var entry in allEntries)
        {
            if (!byLayer.TryGetValue(entry.Layer, out var list))
            {
                list = [];
                byLayer[entry.Layer] = list;
            }
            list.Add(entry);
        }

        return new DisciplineKnowledgeSummary
        {
            DisciplineId = disciplineId,
            ByLayer = byLayer,
            TotalCount = allEntries.Count
        };
    }

    /// <summary>获取统计信息</summary>
    public MemoryStats GetStats() => _store.GetStats();
}
