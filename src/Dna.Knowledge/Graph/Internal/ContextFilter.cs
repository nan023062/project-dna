using System.Text;
using System.Text.Json;
using Dna.Knowledge.Models;
using Dna.Memory.Models;
using Dna.Memory.Store;

namespace Dna.Knowledge;

/// <summary>
/// 上下文过滤器 — 视界由依赖关系推导：
///   Current         = 自己 / activeModules
///   SharedOrSoft    = 已声明依赖（被依赖方对依赖方开放）
///   CrossWorkPeer   = CrossWork 协作方（仅看声明的 Contract）
///   Unlinked        = 无关系
/// </summary>
internal static class ContextFilter
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    internal static ModuleContext BuildContext(
        string targetModule,
        string? currentModule,
        TopologySnapshot topology,
        MemoryStore store,
        IProjectAdapter? adapter,
        List<string>? activeModules)
    {
        var targetNode = topology.Nodes.FirstOrDefault(n =>
            string.Equals(n.Name, targetModule, StringComparison.OrdinalIgnoreCase));

        if (targetNode == null)
        {
            return new ModuleContext
            {
                ModuleName = targetModule,
                Level = ContextLevel.Unlinked,
                BlockMessage = $"模块 '{targetModule}' 不存在于拓扑中"
            };
        }

        var level = DetermineLevel(targetModule, currentModule, topology, activeModules);

        if (level == ContextLevel.Unlinked)
        {
            return new ModuleContext
            {
                ModuleName = targetNode.Name,
                Discipline = targetNode.Discipline,
                Level = ContextLevel.Unlinked,
                BlockMessage = $"模块 '{targetModule}' 不在依赖链或 CrossWork 中，不可访问"
            };
        }

        var identityContent = QueryMemoryContent(store, targetNode.Id, WellKnownTags.Identity);
        var lessonsContent = QueryMemoryContent(store, targetNode.Id, WellKnownTags.Lesson);
        var activeContent = QueryMemoryContent(store, targetNode.Id, WellKnownTags.ActiveTask);

        var contractContent = ExtractContractFromIdentity(identityContent);
        var linksContent = BuildLinksContent(targetNode);

        if (level == ContextLevel.CrossWorkPeer)
        {
            var crossWorkContract = BuildCrossWorkContract(targetModule, currentModule, topology);
            return new ModuleContext
            {
                ModuleName = targetNode.Name,
                Discipline = targetNode.Discipline,
                Level = level,
                ContractContent = crossWorkContract ?? contractContent
            };
        }

        return new ModuleContext
        {
            ModuleName = targetNode.Name,
            Discipline = targetNode.Discipline,
            Level = level,
            IdentityContent = identityContent,
            LessonsContent = lessonsContent,
            LinksContent = linksContent,
            ActiveContent = activeContent,
            ContractContent = contractContent,
            ContentFilePaths = adapter?.GetModuleFiles(targetNode.RelativePath ?? string.Empty) ?? [],
            Summary = targetNode.Summary,
            Boundary = targetNode.Boundary,
            PublicApi = targetNode.PublicApi,
            Constraints = targetNode.Constraints,
            Metadata = targetNode.Metadata
        };
    }

    /// <summary>
    /// 视界推导规则：
    ///   自己 / activeModules → Current
    ///   CrossWork 模块 → 目标非 CW 模块: Current（自由访问）
    ///   CrossWork 模块 → 目标是 CW 模块: Unlinked（CW 之间隔离）
    ///   普通模块 → 目标是 CW 模块: Unlinked（不允许依赖 CW）
    ///   已声明依赖 → SharedOrSoft
    ///   CrossWork 参与方关系 → CrossWorkPeer
    ///   其他 → Unlinked
    /// </summary>
    private static ContextLevel DetermineLevel(
        string target, string? current, TopologySnapshot topology, List<string>? activeModules)
    {
        if (string.IsNullOrEmpty(current))
            return ContextLevel.SharedOrSoft;

        if (string.Equals(target, current, StringComparison.OrdinalIgnoreCase))
            return ContextLevel.Current;

        if (activeModules?.Contains(target, StringComparer.OrdinalIgnoreCase) == true)
            return ContextLevel.Current;

        var currentNode = topology.Nodes.FirstOrDefault(n =>
            n.Name.Equals(current, StringComparison.OrdinalIgnoreCase));
        var targetNode = topology.Nodes.FirstOrDefault(n =>
            n.Name.Equals(target, StringComparison.OrdinalIgnoreCase));

        if (currentNode != null && currentNode.IsCrossWorkModule)
        {
            if (targetNode is { IsCrossWorkModule: true })
                return ContextLevel.Unlinked;
            return ContextLevel.Current;
        }

        if (targetNode is { IsCrossWorkModule: true })
            return ContextLevel.Unlinked;

        var hasDependency = topology.DepMap.TryGetValue(current, out var deps) &&
                            deps.Contains(target, StringComparer.OrdinalIgnoreCase);

        if (hasDependency)
            return ContextLevel.SharedOrSoft;

        var isCrossWorkPeer = topology.CrossWorks.Any(cw =>
            cw.Participants.Any(p => p.ModuleName.Equals(current, StringComparison.OrdinalIgnoreCase)) &&
            cw.Participants.Any(p => p.ModuleName.Equals(target, StringComparison.OrdinalIgnoreCase)));

        if (isCrossWorkPeer)
            return ContextLevel.CrossWorkPeer;

        return ContextLevel.Unlinked;
    }

    private static string? BuildCrossWorkContract(string target, string? current, TopologySnapshot topology)
    {
        if (string.IsNullOrEmpty(current)) return null;

        var sharedCrossWorks = topology.CrossWorks
            .Where(cw =>
                cw.Participants.Any(p => p.ModuleName.Equals(current, StringComparison.OrdinalIgnoreCase)) &&
                cw.Participants.Any(p => p.ModuleName.Equals(target, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (sharedCrossWorks.Count == 0) return null;

        var parts = new List<string>();
        foreach (var cw in sharedCrossWorks)
        {
            var participant = cw.Participants.First(p =>
                p.ModuleName.Equals(target, StringComparison.OrdinalIgnoreCase));

            var section = $"## CrossWork: {cw.Name}\n" +
                          $"- 职责: {participant.Role}\n" +
                          (participant.Contract != null ? $"- Contract: {participant.Contract}\n" : "") +
                          (participant.Deliverable != null ? $"- 交付物: {participant.Deliverable}\n" : "");
            parts.Add(section);
        }

        return string.Join("\n", parts);
    }

    private static string? QueryMemoryContent(MemoryStore store, string moduleId, string tag)
    {
        var entries = store.Query(new MemoryFilter
        {
            Tags = [tag],
            NodeId = moduleId,
            Freshness = FreshnessFilter.All,
            Limit = 5
        });

        var matched = entries.OrderByDescending(e => e.Importance).ToList();

        if (matched.Count == 0) return null;

        return tag switch
        {
            WellKnownTags.Identity => FormatIdentityContents(matched),
            WellKnownTags.Lesson => FormatLessonContents(matched),
            WellKnownTags.ActiveTask => FormatActiveTaskContents(matched),
            _ => string.Join("\n\n", matched.Select(e => e.Content))
        };
    }

    private static string BuildLinksContent(KnowledgeNode node)
    {
        var allDeps = node.Dependencies
            .Union(node.ComputedDependencies, StringComparer.OrdinalIgnoreCase)
            .OrderBy(d => d)
            .ToList();
        if (allDeps.Count == 0) return "[]";

        var declared = new HashSet<string>(node.Dependencies, StringComparer.OrdinalIgnoreCase);
        var computed = new HashSet<string>(node.ComputedDependencies, StringComparer.OrdinalIgnoreCase);
        var parts = allDeps.Select(dep => new
        {
            name = dep,
            declared = declared.Contains(dep),
            computed = computed.Contains(dep)
        });

        return JsonSerializer.Serialize(parts, JsonOpts);
    }

    private static string? ExtractContractFromIdentity(string? identityContent)
    {
        if (string.IsNullOrWhiteSpace(identityContent)) return null;
        try
        {
            var payload = JsonSerializer.Deserialize<IdentityPayload>(identityContent, JsonOpts);
            return payload?.Contract;
        }
        catch
        {
            return null;
        }
    }

    private static string FormatIdentityContents(List<MemoryEntry> entries)
    {
        var sb = new StringBuilder();
        foreach (var e in entries)
        {
            try
            {
                var payload = JsonSerializer.Deserialize<IdentityPayload>(e.Content, JsonOpts);
                if (payload == null) continue;

                sb.AppendLine($"summary: {payload.Summary}");
                if (!string.IsNullOrWhiteSpace(payload.Contract))
                    sb.AppendLine($"contract: {payload.Contract}");
                if (payload.Keywords.Count > 0)
                    sb.AppendLine($"keywords: {string.Join(", ", payload.Keywords)}");
                if (!string.IsNullOrWhiteSpace(payload.Description))
                    sb.AppendLine($"description: {payload.Description}");
                sb.AppendLine();
            }
            catch
            {
                // skip malformed payload
            }
        }

        return sb.ToString().Trim();
    }

    private static string FormatLessonContents(List<MemoryEntry> entries)
    {
        var sb = new StringBuilder();
        foreach (var e in entries)
        {
            try
            {
                var payload = JsonSerializer.Deserialize<LessonPayload>(e.Content, JsonOpts);
                if (payload == null) continue;

                sb.AppendLine($"- {payload.Title}");
                if (!string.IsNullOrWhiteSpace(payload.Severity))
                    sb.AppendLine($"  severity: {payload.Severity}");
                sb.AppendLine($"  context: {payload.Context}");
                if (!string.IsNullOrWhiteSpace(payload.Resolution))
                    sb.AppendLine($"  resolution: {payload.Resolution}");
                if (payload.Tags.Count > 0)
                    sb.AppendLine($"  tags: {string.Join(", ", payload.Tags)}");
            }
            catch
            {
                // skip malformed payload
            }
        }
        return sb.ToString().Trim();
    }

    private static string FormatActiveTaskContents(List<MemoryEntry> entries)
    {
        var sb = new StringBuilder();
        foreach (var e in entries)
        {
            try
            {
                var payload = JsonSerializer.Deserialize<ActiveTaskPayload>(e.Content, JsonOpts);
                if (payload == null) continue;

                sb.AppendLine($"task: {payload.Task}");
                if (!string.IsNullOrWhiteSpace(payload.Status))
                    sb.AppendLine($"status: {payload.Status}");
                if (!string.IsNullOrWhiteSpace(payload.Assignee))
                    sb.AppendLine($"assignee: {payload.Assignee}");
                if (payload.RelatedModules.Count > 0)
                    sb.AppendLine($"relatedModules: {string.Join(", ", payload.RelatedModules)}");
                if (!string.IsNullOrWhiteSpace(payload.Notes))
                    sb.AppendLine($"notes: {payload.Notes}");
                sb.AppendLine();
            }
            catch
            {
                // skip malformed payload
            }
        }
        return sb.ToString().Trim();
    }
}
