using Dna.Memory.Models;
using Dna.Memory.Store;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

namespace Dna.Knowledge.Governance;

/// <summary>
/// 记忆维护器 — 负责知识蒸馏、冲突检测和冗余合并
/// </summary>
internal class MemoryMaintainer
{
    private readonly MemoryStore _store;
    private readonly ILogger<MemoryMaintainer> _logger;
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

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

    /// <summary>
    /// 按模块压缩记忆：将短期记忆提炼为模块 Identity，并归档已提炼的 Episodic/Working 记忆。
    /// </summary>
    public async Task<KnowledgeCondenseResult> CondenseNodeKnowledgeAsync(
        TopologySnapshot topology,
        string nodeIdOrName,
        int maxSourceMemories = 200)
    {
        if (string.IsNullOrWhiteSpace(nodeIdOrName))
            throw new ArgumentException("nodeIdOrName 不能为空。", nameof(nodeIdOrName));

        var node = ResolveNode(topology, nodeIdOrName.Trim())
            ?? throw new InvalidOperationException($"节点不存在: {nodeIdOrName}");

        var source = _store.Query(new MemoryFilter
        {
            NodeId = node.Id,
            Freshness = FreshnessFilter.FreshAndAging,
            Limit = Math.Clamp(maxSourceMemories, 20, 2000)
        });

        source = source
            .Where(m => !m.Tags.Contains("#condensed", StringComparer.OrdinalIgnoreCase))
            .OrderByDescending(m => m.Importance)
            .ThenByDescending(m => m.CreatedAt)
            .ToList();

        if (source.Count == 0)
        {
            return new KnowledgeCondenseResult
            {
                NodeId = node.Id,
                NodeName = node.Name,
                SourceCount = 0,
                ArchivedCount = 0,
                Summary = "无可压缩记忆"
            };
        }

        var identity = BuildIdentityPayload(node, source);
        var identityRequest = new RememberRequest
        {
            Type = MemoryType.Structural,
            NodeType = node.Type,
            Source = MemorySource.System,
            NodeId = node.Id,
            Disciplines = string.IsNullOrWhiteSpace(node.Discipline) ? [] : [node.Discipline],
            Tags = [WellKnownTags.Identity, "#condensed"],
            Summary = identity.Summary,
            Content = JsonSerializer.Serialize(identity, JsonOpts),
            Importance = 0.9
        };

        var condensed = await _store.RememberAsync(identityRequest);

        // 归档已提炼的短期记忆（避免持续膨胀）
        var archivedCount = 0;
        foreach (var m in source)
        {
            if (m.Id == condensed.Id) continue;
            if (m.Type is MemoryType.Episodic or MemoryType.Working)
            {
                _store.UpdateFreshness(m.Id, FreshnessStatus.Archived);
                archivedCount++;
            }
        }

        // 历史 identity 仅保留最新一条为 Fresh（其余归档）
        var identities = _store.Query(new MemoryFilter
        {
            NodeId = node.Id,
            Tags = [WellKnownTags.Identity],
            Freshness = FreshnessFilter.All,
            Limit = 200
        }).OrderByDescending(x => x.CreatedAt).ToList();
        foreach (var old in identities.Skip(1))
        {
            if (old.Freshness != FreshnessStatus.Archived)
            {
                _store.UpdateFreshness(old.Id, FreshnessStatus.Archived);
            }
        }

        var knowledgeView = BuildNodeKnowledgeView(identity, source, condensed);
        _store.UpsertNodeKnowledge(node.Id, knowledgeView);

        _logger.LogInformation("模块知识压缩完成: node={Node} source={Source} archived={Archived} identity={IdentityId}",
            node.Name, source.Count, archivedCount, condensed.Id);

        return new KnowledgeCondenseResult
        {
            NodeId = node.Id,
            NodeName = node.Name,
            SourceCount = source.Count,
            ArchivedCount = archivedCount,
            NewIdentityMemoryId = condensed.Id,
            Summary = identity.Summary
        };
    }

    /// <summary>
    /// 全量压缩所有模块节点。
    /// </summary>
    public async Task<List<KnowledgeCondenseResult>> CondenseAllNodesAsync(
        TopologySnapshot topology,
        int maxSourceMemories = 200)
    {
        var results = new List<KnowledgeCondenseResult>();
        foreach (var node in topology.Nodes.Where(n => n.Type is NodeType.Technical or NodeType.Team))
        {
            try
            {
                var result = await CondenseNodeKnowledgeAsync(topology, node.Id, maxSourceMemories);
                results.Add(result);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "模块知识压缩失败: node={Node}", node.Name);
            }
        }
        return results;
    }

    private static KnowledgeNode? ResolveNode(TopologySnapshot topology, string nodeIdOrName)
    {
        return topology.Nodes.FirstOrDefault(n =>
            string.Equals(n.Id, nodeIdOrName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(n.Name, nodeIdOrName, StringComparison.OrdinalIgnoreCase));
    }

    private static IdentityPayload BuildIdentityPayload(KnowledgeNode node, List<MemoryEntry> source)
    {
        var previousIdentity = source
            .Where(m => m.Tags.Contains(WellKnownTags.Identity, StringComparer.OrdinalIgnoreCase))
            .OrderByDescending(m => m.CreatedAt)
            .Select(ParseIdentityPayload)
            .FirstOrDefault(p => p != null);

        var lessons = source
            .Where(m => m.Tags.Contains(WellKnownTags.Lesson, StringComparer.OrdinalIgnoreCase))
            .Select(ParseLessonPayload)
            .Where(p => p != null)
            .Take(5)
            .ToList();

        var activeTasks = source
            .Where(m => m.Tags.Contains(WellKnownTags.ActiveTask, StringComparer.OrdinalIgnoreCase))
            .Select(ParseActiveTaskPayload)
            .Where(p => p != null)
            .Take(5)
            .ToList();

        var summaryBase = previousIdentity?.Summary
            ?? node.Summary
            ?? $"模块 {node.Name} 的长期知识摘要";

        var detail = new StringBuilder();
        if (lessons.Count > 0)
        {
            detail.AppendLine("近期教训:");
            foreach (var l in lessons)
                detail.AppendLine($"- {l!.Title}: {l.Context}");
        }
        if (activeTasks.Count > 0)
        {
            if (detail.Length > 0) detail.AppendLine();
            detail.AppendLine("当前任务:");
            foreach (var t in activeTasks)
                detail.AppendLine($"- {t!.Task} ({t.Status ?? "unknown"})");
        }

        var keywords = source
            .SelectMany(m => m.Tags)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();

        return new IdentityPayload
        {
            Summary = summaryBase,
            Contract = previousIdentity?.Contract ?? node.Contract,
            Description = detail.Length == 0 ? null : detail.ToString().Trim(),
            Keywords = keywords
        };
    }

    private static IdentityPayload? ParseIdentityPayload(MemoryEntry entry)
    {
        try
        {
            return JsonSerializer.Deserialize<IdentityPayload>(entry.Content, JsonOpts);
        }
        catch
        {
            return null;
        }
    }

    private static LessonPayload? ParseLessonPayload(MemoryEntry entry)
    {
        try
        {
            return JsonSerializer.Deserialize<LessonPayload>(entry.Content, JsonOpts);
        }
        catch
        {
            return new LessonPayload
            {
                Title = entry.Summary ?? "untitled",
                Context = entry.Content
            };
        }
    }

    private static ActiveTaskPayload? ParseActiveTaskPayload(MemoryEntry entry)
    {
        try
        {
            return JsonSerializer.Deserialize<ActiveTaskPayload>(entry.Content, JsonOpts);
        }
        catch
        {
            return new ActiveTaskPayload
            {
                Task = entry.Summary ?? "unknown",
                Notes = entry.Content
            };
        }
    }

    private static NodeKnowledge BuildNodeKnowledgeView(
        IdentityPayload identity,
        List<MemoryEntry> source,
        MemoryEntry condensed)
    {
        var lessons = source
            .Where(m => m.Tags.Contains(WellKnownTags.Lesson, StringComparer.OrdinalIgnoreCase))
            .Select(ParseLessonPayload)
            .Where(p => p != null)
            .Take(10)
            .Select(p => new LessonSummary
            {
                Title = p!.Title,
                Severity = p.Severity,
                Resolution = p.Resolution
            })
            .ToList();

        var activeTasks = source
            .Where(m => m.Tags.Contains(WellKnownTags.ActiveTask, StringComparer.OrdinalIgnoreCase))
            .Select(ParseActiveTaskPayload)
            .Where(p => p != null && !string.IsNullOrWhiteSpace(p.Task))
            .Take(10)
            .Select(p => p!.Task)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var facts = source
            .OrderByDescending(m => m.Importance)
            .ThenByDescending(m => m.CreatedAt)
            .Select(m => m.Summary)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Take(20)
            .Select(s => s!)
            .ToList();

        var memoryIds = source
            .Select(m => m.Id)
            .Concat([condensed.Id])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(200)
            .ToList();

        return new NodeKnowledge
        {
            Identity = identity.Summary,
            Lessons = lessons,
            ActiveTasks = activeTasks,
            Facts = facts,
            TotalMemoryCount = source.Count,
            MemoryIds = memoryIds
        };
    }
}
