using Dna.Knowledge;
using Dna.Memory.Models;
using Dna.Memory.Store;
using Microsoft.Extensions.Logging;

namespace Dna.Memory.Services;

/// <summary>
/// 记忆召回引擎 — recall 的完整实现。
/// 四通道召回（向量 + 标签 + FTS + 坐标匹配）→ 融合排序 → 鲜活度过滤 → 约束链展开。
/// </summary>
internal class MemoryRecallEngine
{
    private const double AlphaVector = 0.35;
    private const double BetaCoordinate = 0.20;
    private const double GammaTag = 0.10;
    private const double DeltaImportance = 0.15;
    private const double EpsilonFreshness = 0.10;
    private const double ZetaRecency = 0.10;

    private const int MaxConstraintPerNodeType = 3;
    private const int MaxConstraintTotal = 15;

    private readonly MemoryStore _store;
    private readonly VectorIndex _vectorIndex;
    private readonly EmbeddingService _embeddingService;
    private readonly ILogger<MemoryRecallEngine> _logger;

    public MemoryRecallEngine(
        MemoryStore store,
        VectorIndex vectorIndex,
        EmbeddingService embeddingService,
        ILogger<MemoryRecallEngine> logger)
    {
        _store = store;
        _vectorIndex = vectorIndex;
        _embeddingService = embeddingService;
        _logger = logger;
    }

    /// <summary>
    /// 语义召回 — 四通道检索 + 融合排序 + 约束链展开。
    /// </summary>
    public async Task<RecallResult> RecallAsync(RecallQuery query)
    {
        var isVectorDegraded = false;

        // ═══ 通道 1：向量语义检索（主通道） ═══
        var vectorResults = new List<(string Id, double Score)>();
        var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query.Question);
        if (queryEmbedding != null && _vectorIndex.Count > 0)
        {
            vectorResults = _vectorIndex.Search(queryEmbedding, query.MaxResults * 3);
        }
        else
        {
            isVectorDegraded = true;
        }

        // ═══ 通道 2：FTS 全文检索（兜底 / 辅助） ═══
        var ftsResults = _store.FullTextSearch(query.Question, query.MaxResults * 2);

        // ═══ 通道 3：标签精确匹配 ═══
        var tagEntries = new List<MemoryEntry>();
        if (query.Tags is { Count: > 0 })
        {
            tagEntries = _store.Query(new MemoryFilter
            {
                Tags = query.Tags,
                Freshness = query.Freshness,
                Limit = query.MaxResults * 2
            });
        }

        // ═══ 通道 4：坐标匹配（Discipline + Feature + Module + NodeType） ═══
        var coordEntries = new List<MemoryEntry>();
        if (query.Disciplines is { Count: > 0 } || query.Features is { Count: > 0 } || !string.IsNullOrEmpty(query.NodeId))
        {
            coordEntries = _store.Query(new MemoryFilter
            {
                Disciplines = query.Disciplines,
                Features = query.Features,
                NodeId = query.NodeId,
                NodeTypes = query.ResolvedNodeTypes,
                Freshness = query.Freshness,
                Limit = query.MaxResults * 2
            });
        }

        // ═══ 融合排序 ═══
        var candidates = MergeCandidates(query, vectorResults, ftsResults, tagEntries, coordEntries);

        // ═══ 鲜活度过滤 ═══
        var filtered = ApplyFreshnessFilter(candidates, query.Freshness);

        // ═══ Top-K ═══
        var topK = filtered
            .OrderByDescending(s => s.Score)
            .Take(query.MaxResults)
            .ToList();

        // ═══ 约束链展开 ═══
        var constraintChain = new List<MemoryEntry>();
        if (query.ExpandConstraintChain && topK.Count > 0)
        {
            constraintChain = ExpandConstraintChain(topK, query);
        }

        var confidence = topK.Count > 0 ? topK.Average(s => s.Score) : 0;

        _logger.LogInformation(
            "recall: q=\"{Question}\" → {Count} results (vector={VCount}, fts={FCount}, tag={TCount}, coord={CCount}) degraded={Degraded}",
            Truncate(query.Question, 40), topK.Count,
            vectorResults.Count, ftsResults.Count, tagEntries.Count, coordEntries.Count,
            isVectorDegraded);

        return new RecallResult
        {
            Memories = topK,
            ConstraintChain = constraintChain,
            Confidence = confidence,
            IsVectorDegraded = isVectorDegraded,
            SuggestedFollowUps = GenerateFollowUps(topK, query)
        };
    }

    // ═══════════════════════════════════════════
    //  融合排序
    // ═══════════════════════════════════════════

    private Dictionary<string, ScoredMemory> MergeCandidates(
        RecallQuery query,
        List<(string Id, double Score)> vectorResults,
        List<(string Id, double Rank)> ftsResults,
        List<MemoryEntry> tagEntries,
        List<MemoryEntry> coordEntries)
    {
        var merged = new Dictionary<string, ScoredMemory>();

        var vectorMax = vectorResults.Count > 0 ? vectorResults.Max(v => v.Score) : 1.0;
        foreach (var (id, score) in vectorResults)
        {
            var entry = _store.GetById(id);
            if (entry == null) continue;

            var normalizedVector = vectorMax > 0 ? score / vectorMax : 0;
            var finalScore = ComputeFinalScore(entry, query, normalizedVector, "vector");
            merged[id] = new ScoredMemory { Entry = entry, Score = finalScore, MatchChannel = "vector" };
        }

        var ftsMax = ftsResults.Count > 0 ? ftsResults.Max(f => Math.Abs(f.Rank)) : 1.0;
        foreach (var (id, rank) in ftsResults)
        {
            var normalizedFts = ftsMax > 0 ? Math.Abs(rank) / ftsMax : 0;

            if (merged.TryGetValue(id, out var existing))
            {
                existing.Score = Math.Max(existing.Score, existing.Score + normalizedFts * 0.1);
            }
            else
            {
                var entry = _store.GetById(id);
                if (entry == null) continue;
                var finalScore = ComputeFinalScore(entry, query, normalizedFts * 0.5, "fts");
                merged[id] = new ScoredMemory { Entry = entry, Score = finalScore, MatchChannel = "fts" };
            }
        }

        foreach (var entry in tagEntries)
        {
            if (merged.TryGetValue(entry.Id, out var existing))
            {
                existing.Score += GammaTag * 0.5;
            }
            else
            {
                var finalScore = ComputeFinalScore(entry, query, 0, "tag");
                merged[entry.Id] = new ScoredMemory { Entry = entry, Score = finalScore, MatchChannel = "tag" };
            }
        }

        foreach (var entry in coordEntries)
        {
            if (merged.TryGetValue(entry.Id, out var existing))
            {
                existing.Score += BetaCoordinate * 0.3;
            }
            else
            {
                var finalScore = ComputeFinalScore(entry, query, 0, "coordinate");
                merged[entry.Id] = new ScoredMemory { Entry = entry, Score = finalScore, MatchChannel = "coordinate" };
            }
        }

        return merged;
    }

    private double ComputeFinalScore(MemoryEntry entry, RecallQuery query, double primaryScore, string channel)
    {
        var vectorComponent = channel == "vector" ? AlphaVector * primaryScore : 0;
        var ftsComponent = channel == "fts" ? AlphaVector * primaryScore : 0;
        var coordComponent = BetaCoordinate * CoordinateMatch(entry, query);
        var tagComponent = GammaTag * TagOverlap(entry, query);
        var importanceComponent = DeltaImportance * entry.Importance;
        var freshnessComponent = EpsilonFreshness * FreshnessWeight(entry.Freshness);
        var recencyComponent = ZetaRecency * RecencyWeight(entry.CreatedAt);

        return vectorComponent + ftsComponent + coordComponent + tagComponent
               + importanceComponent + freshnessComponent + recencyComponent;
    }

    private static double CoordinateMatch(MemoryEntry entry, RecallQuery query)
    {
        var score = 0.0;
        if (query.Disciplines?.Intersect(entry.Disciplines, StringComparer.OrdinalIgnoreCase).Any() == true)
            score += 0.4;
        if (query.Features?.Intersect(entry.Features, StringComparer.OrdinalIgnoreCase).Any() == true)
            score += 0.4;
        if (!string.IsNullOrEmpty(query.NodeId) && string.Equals(query.NodeId, entry.NodeId, StringComparison.OrdinalIgnoreCase))
            score += 0.4;
        if (query.ResolvedNodeTypes?.Contains(entry.NodeType) == true)
            score += 0.2;
        return score;
    }

    private static double TagOverlap(MemoryEntry entry, RecallQuery query)
    {
        if (query.Tags == null || query.Tags.Count == 0 || entry.Tags.Count == 0) return 0;
        var overlap = query.Tags.Intersect(entry.Tags, StringComparer.OrdinalIgnoreCase).Count();
        return (double)overlap / Math.Max(query.Tags.Count, 1);
    }

    private static double FreshnessWeight(FreshnessStatus freshness) => freshness switch
    {
        FreshnessStatus.Fresh => 1.0,
        FreshnessStatus.Aging => 0.7,
        FreshnessStatus.Stale => 0.3,
        FreshnessStatus.Superseded => 0.1,
        FreshnessStatus.Archived => 0.05,
        _ => 0.5
    };

    private static double RecencyWeight(DateTime createdAt)
    {
        var daysSinceCreation = (DateTime.UtcNow - createdAt).TotalDays;
        return Math.Max(0, 1.0 - daysSinceCreation / 365.0);
    }

    // ═══════════════════════════════════════════
    //  鲜活度过滤
    // ═══════════════════════════════════════════

    private static List<ScoredMemory> ApplyFreshnessFilter(
        Dictionary<string, ScoredMemory> candidates, FreshnessFilter filter)
    {
        return filter switch
        {
            FreshnessFilter.FreshOnly =>
                candidates.Values.Where(s => s.Entry.Freshness == FreshnessStatus.Fresh).ToList(),
            FreshnessFilter.FreshAndAging =>
                candidates.Values.Where(s => s.Entry.Freshness is FreshnessStatus.Fresh or FreshnessStatus.Aging).ToList(),
            FreshnessFilter.IncludeStale =>
                candidates.Values.Where(s => s.Entry.Freshness is FreshnessStatus.Fresh or FreshnessStatus.Aging or FreshnessStatus.Stale).ToList(),
            _ => candidates.Values.ToList()
        };
    }

    // ═══════════════════════════════════════════
    //  约束链展开
    // ═══════════════════════════════════════════

    /// <summary>
    /// 核心创新：AI 不只是搜到相关记忆，而是沿治理层级自动展开约束。
    /// 从 top-K 结果中推断知识坐标，向上召回 Project/Department/Group 的约束。
    /// </summary>
    private List<MemoryEntry> ExpandConstraintChain(List<ScoredMemory> topK, RecallQuery query)
    {
        var chain = new List<MemoryEntry>();
        var visited = new HashSet<string>();

        foreach (var scored in topK)
        {
            var entryChain = _store.GetConstraintChain(scored.Entry.Id);
            foreach (var e in entryChain)
            {
                if (visited.Add(e.Id) && e.Id != scored.Entry.Id)
                    chain.Add(e);
            }
        }

        var disciplines = topK.SelectMany(s => s.Entry.Disciplines).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var features = topK.SelectMany(s => s.Entry.Features).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        foreach (var nodeType in new[] { NodeType.Project, NodeType.Department, NodeType.Group })
        {
            if (chain.Count >= MaxConstraintTotal) break;

            var upperMemories = _store.Query(new MemoryFilter
            {
                NodeTypes = [nodeType],
                Disciplines = disciplines.Count > 0 ? disciplines : null,
                Features = features.Count > 0 ? features : null,
                Freshness = FreshnessFilter.FreshAndAging,
                Limit = MaxConstraintPerNodeType
            });

            foreach (var m in upperMemories)
            {
                if (chain.Count >= MaxConstraintTotal) break;
                if (visited.Add(m.Id))
                    chain.Add(m);
            }
        }

        chain.Sort((a, b) => NodeTypeCompat.GovernanceOrder(a.NodeType).CompareTo(NodeTypeCompat.GovernanceOrder(b.NodeType)));
        return chain;
    }

    // ═══════════════════════════════════════════
    //  跟进建议
    // ═══════════════════════════════════════════

    private static List<string> GenerateFollowUps(List<ScoredMemory> results, RecallQuery query)
    {
        var suggestions = new List<string>();

        var nodeTypes = results.Select(r => r.Entry.NodeType).Distinct().ToList();
        if (!nodeTypes.Contains(NodeType.Department))
            suggestions.Add("可以查看 Department 节点约束，补齐部门级标准");
        if (!nodeTypes.Contains(NodeType.Group))
            suggestions.Add("可以查看 Group 节点规范，补齐技术组级约束");

        var types = results.Select(r => r.Entry.Type).Distinct().ToList();
        if (!types.Contains(MemoryType.Episodic))
            suggestions.Add("可以查看相关教训（Episodic）了解历史踩坑");
        if (!types.Contains(MemoryType.Procedural))
            suggestions.Add("可以查看操作流程（Procedural）了解最佳实践");

        return suggestions.Take(3).ToList();
    }

    private static string Truncate(string text, int maxLen)
        => text.Length <= maxLen ? text : text[..maxLen] + "…";
}
