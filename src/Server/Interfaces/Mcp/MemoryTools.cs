using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dna.Core.Config;
using Dna.Core.Logging;
using Dna.Knowledge;
using Dna.Knowledge.Models;
using Dna.Memory.Models;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Dna.Interfaces.Mcp;

/// <summary>
/// 项目记忆系统 MCP 工具集 — remember / recall / verify_memory / get_feature_knowledge
/// </summary>
[McpServerToolType]
public class MemoryTools(
    IMemoryEngine memory,
    ProjectConfig config,
    ILogger<MemoryTools> logger)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    [McpServerTool, Description(
        "写入一条项目记忆。完成任务或踩坑时应主动调用，确保知识沉淀不丢失。" +
        "系统自动生成向量嵌入、写入 SQLite 索引和 JSON 文件。" +
        "参数 content：知识正文（纯文本或 JSON）。" +
        "系统标签要求 JSON content — " +
        "#identity: {\"name\":\"模块名\",\"responsibility\":\"职责\",\"boundary\":\"边界\"} | " +
        "#lesson: {\"context\":\"背景\",\"problem\":\"问题\",\"solution\":\"解决方案\",\"impact\":\"影响\"} | " +
        "#active-task: {\"goal\":\"目标\",\"modules\":[\"模块\"],\"status\":\"进行中\"}。" +
        "普通知识用纯文本即可。" +
        "参数 type：记忆类型 — Structural(结构)/Semantic(语义)/Episodic(事件)/Working(工作)/Procedural(过程)。" +
        "参数 layer：知识层级 — ProjectVision(L0)/DisciplineStandard(L1)/CrossDiscipline(L2)/FeatureSystem(L3)/Implementation(L4)。" +
        "参数 disciplines：关联职能域（逗号分隔）— engineering/design/art/ta/audio/devops/qa。" +
        "参数 tags：标签（逗号分隔）— 如 #lesson,#bug-fix,#performance。" +
        "参数 summary：可选，一句话摘要（留空则自动生成）。" +
        "参数 features：可选，关联业务系统（逗号分隔）— 如 character,building。" +
        "参数 nodeId：可选，所属节点 ID（单个）。" +
        "参数 parentId：可选，上层约束记忆的 ID（用于建立约束链）。" +
        "参数 importance：可选，重要度 0.0-1.0，默认 0.5。")]
    public async Task<string> remember(
        [Description("知识正文（文本或 JSON）")] string content,
        [Description("记忆类型: Structural/Semantic/Episodic/Working/Procedural")] string type,
        [Description("知识层级: ProjectVision/DisciplineStandard/CrossDiscipline/FeatureSystem/Implementation")] string layer,
        [Description("关联职能域，逗号分隔: engineering/design/art/ta/audio/devops/qa")] string disciplines,
        [Description("标签，逗号分隔: 如 #lesson,#bug-fix")] string? tags = null,
        [Description("一句话摘要（留空自动生成）")] string? summary = null,
        [Description("关联业务系统，逗号分隔: 如 character,building")] string? features = null,
        [Description("所属节点 ID")] string? nodeId = null,
        [Description("上层约束记忆 ID（用于约束链）")] string? parentId = null,
        [Description("重要度 0.0-1.0")] double importance = 0.5,
        [Description("项目根目录（留空自动使用当前项目）")] string? projectRoot = null)
    {
        logger.LogInformation(LogEvents.Mcp, "remember() type={Type} layer={Layer}", type, layer);
        EnsureKnowledge(projectRoot);

        if (!Enum.TryParse<MemoryType>(type, true, out var memoryType))
            return $"错误：无效的记忆类型 '{type}'。可选值: Structural, Semantic, Episodic, Working, Procedural";
        if (!Enum.TryParse<KnowledgeLayer>(layer, true, out var knowledgeLayer))
            return $"错误：无效的知识层级 '{layer}'。可选值: ProjectVision, DisciplineStandard, CrossDiscipline, FeatureSystem, Implementation";

        var request = new RememberRequest
        {
            Content = content,
            Type = memoryType,
            Layer = knowledgeLayer,
            Source = MemorySource.Ai,
            Summary = summary,
            Disciplines = SplitCsv(disciplines),
            Features = SplitCsvOrNull(features),
            NodeId = nodeId,
            Tags = SplitCsv(tags ?? ""),
            ParentId = parentId,
            Importance = Math.Clamp(importance, 0, 1)
        };

        var entry = await memory.RememberAsync(request);
        return $"✓ 记忆已写入 [{entry.Id}]\n类型: {entry.Type} | 层级: {entry.Layer} | 鲜活度: Fresh\n摘要: {entry.Summary}";
    }

    [McpServerTool, Description(
        "语义检索项目记忆。遇到不确定的规范、流程、约定时应主动调用，NEVER 凭猜测行事。" +
        "输入自然语言问题，系统通过向量语义搜索 + 标签匹配 + 全文检索 + 坐标匹配 四通道召回，" +
        "自动沿知识层级展开约束链（L0→L1→L2→L3 的上层约束也会返回）。" +
        "参数 question：自然语言问题，如「上次改角色换装遇到什么问题？」。" +
        "参数 disciplines：可选，限定职能域（逗号分隔）。" +
        "参数 features：可选，限定业务系统（逗号分隔）。" +
        "参数 nodeId：可选，限定节点 ID（单个）。" +
        "参数 tags：可选，精确匹配标签（逗号分隔）。" +
        "参数 layers：可选，限定知识层级（逗号分隔）。" +
        "参数 expandChain：是否展开约束链，默认 true。" +
        "参数 maxResults：最多返回条数，默认 10。" +
        "When to call: whenever you're uncertain about conventions, standards, past decisions, or lessons learned.")]
    public async Task<string> recall(
        [Description("自然语言问题")] string question,
        [Description("限定职能域，逗号分隔")] string? disciplines = null,
        [Description("限定业务系统，逗号分隔")] string? features = null,
        [Description("限定节点 ID")] string? nodeId = null,
        [Description("精确匹配标签，逗号分隔")] string? tags = null,
        [Description("限定知识层级，逗号分隔: ProjectVision/DisciplineStandard/CrossDiscipline/FeatureSystem/Implementation")] string? layers = null,
        [Description("是否展开约束链")] bool expandChain = true,
        [Description("最多返回条数")] int maxResults = 10,
        [Description("项目根目录（留空自动使用当前项目）")] string? projectRoot = null)
    {
        logger.LogInformation(LogEvents.Mcp, "recall() q=\"{Question}\"", question);
        EnsureKnowledge(projectRoot);

        try
        {
            var decayed = memory.DecayStaleMemories();
            if (decayed > 0)
            {
                logger.LogInformation(LogEvents.Mcp, "recall() 自动降级了 {Count} 条过期记忆", decayed);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "自动降级过期记忆失败，忽略并继续检索");
        }

        var query = new RecallQuery
        {
            Question = question,
            Disciplines = SplitCsvOrNull(disciplines),
            Features = SplitCsvOrNull(features),
            NodeId = nodeId,
            Tags = SplitCsvOrNull(tags),
            Layers = ParseLayers(layers),
            ExpandConstraintChain = expandChain,
            MaxResults = Math.Clamp(maxResults, 1, 50)
        };

        var result = await memory.RecallAsync(query);
        return FormatRecallResult(result);
    }

    [McpServerTool, Description(
        "确认一条记忆仍然有效，重置其鲜活度为 Fresh。" +
        "AI 在使用某条记忆并确认其仍正确时调用，使有效知识保持新鲜。")]
    public string verify_memory(
        [Description("记忆 ID")] string memoryId,
        [Description("项目根目录（留空自动使用当前项目）")] string? projectRoot = null)
    {
        logger.LogInformation(LogEvents.Mcp, "verify_memory() id={Id}", memoryId);
        EnsureKnowledge(projectRoot);
        var entry = memory.GetMemoryById(memoryId);
        if (entry == null)
            return $"错误：记忆不存在 [{memoryId}]";

        memory.VerifyMemory(memoryId);
        return $"✓ 记忆 [{memoryId}] 已验证为有效，鲜活度已重置为 Fresh";
    }

    [McpServerTool, Description(
        "获取业务系统（Feature）的全职能知识汇总。" +
        "返回该系统在所有职能域（程序/策划/美术/TA/音频/DevOps/QA）的知识，以及跨职能协议。" +
        "参数 featureId：业务系统 ID，如 character、building、fishing。")]
    public string get_feature_knowledge(
        [Description("业务系统 ID，如 character、building、fishing")] string featureId,
        [Description("项目根目录（留空自动使用当前项目）")] string? projectRoot = null)
    {
        logger.LogInformation(LogEvents.Mcp, "get_feature_knowledge() feature={Feature}", featureId);
        EnsureKnowledge(projectRoot);
        var summary = memory.GetFeatureSummary(featureId);
        return FormatFeatureSummary(summary);
    }

    [McpServerTool, Description(
        "批量写入多条项目记忆。适用于从文档蒸馏、首次灌入项目知识等场景。" +
        "每条 entry 是一个 JSON 对象，字段与 remember 相同：content(必填), type(必填), layer(必填), disciplines(必填), " +
        "tags, summary, features, nodeId, parentId, importance。" +
        "单次上限 50 条。返回成功/失败计数和写入的 ID 列表。")]
    public async Task<string> batch_remember(
        [Description("JSON 数组，每个元素包含 content/type/layer/disciplines 等字段")] string entries,
        [Description("项目根目录（留空自动使用当前项目）")] string? projectRoot = null)
    {
        logger.LogInformation(LogEvents.Mcp, "batch_remember()");
        EnsureKnowledge(projectRoot);

        List<BatchEntry>? items;
        try
        {
            items = JsonSerializer.Deserialize<List<BatchEntry>>(entries, JsonOpts);
        }
        catch (Exception ex)
        {
            return $"错误：entries 解析失败 — {ex.Message}\n期望格式: [{{\n  \"content\": \"...\",\n  \"type\": \"Semantic\",\n  \"layer\": \"DisciplineStandard\",\n  \"disciplines\": \"engineering\"\n}}]";
        }

        if (items == null || items.Count == 0)
            return "错误：entries 为空";
        if (items.Count > 50)
            return $"错误：单次最多 50 条，当前 {items.Count} 条";

        var requests = new List<RememberRequest>();
        var errors = new List<string>();
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (string.IsNullOrWhiteSpace(item.Content))
            { errors.Add($"[{i}] content 为空"); continue; }
            if (!Enum.TryParse<MemoryType>(item.Type, true, out var mt))
            { errors.Add($"[{i}] 无效 type '{item.Type}'"); continue; }
            if (!Enum.TryParse<KnowledgeLayer>(item.Layer, true, out var kl))
            { errors.Add($"[{i}] 无效 layer '{item.Layer}'"); continue; }

            requests.Add(new RememberRequest
            {
                Content = item.Content,
                Type = mt,
                Layer = kl,
                Source = MemorySource.Ai,
                Summary = item.Summary,
                Disciplines = SplitCsv(item.Disciplines ?? ""),
                Features = SplitCsvOrNull(item.Features),
                NodeId = item.NodeId,
                Tags = SplitCsv(item.Tags ?? ""),
                ParentId = item.ParentId,
                Importance = Math.Clamp(item.Importance ?? 0.5, 0, 1)
            });
        }

        if (requests.Count == 0)
            return $"错误：全部条目校验失败\n{string.Join("\n", errors)}";

        var results = await memory.RememberBatchAsync(requests);
        var ids = results.Select(r => r.Id).ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"✓ 批量写入完成：成功 {results.Count} 条，失败 {errors.Count} 条");
        if (errors.Count > 0)
        {
            sb.AppendLine("\n失败详情：");
            foreach (var e in errors) sb.AppendLine($"  {e}");
        }
        sb.AppendLine($"\n写入 ID：{string.Join(", ", ids)}");
        return sb.ToString();
    }

    [McpServerTool, Description(
        "修改一条已有的项目记忆。用于：知识过时需要更新内容、标签分类错误需要修正、补充缺失的摘要或关联信息。" +
        "只需传入要修改的字段，未传的字段保持不变。" +
        "修改后自动更新全文索引和向量嵌入。")]
    public async Task<string> update_memory(
        [Description("要修改的记忆 ID")] string id,
        [Description("新的知识正文（不修改则留空）")] string? content = null,
        [Description("新的摘要（不修改则留空）")] string? summary = null,
        [Description("新的记忆类型: Structural/Semantic/Episodic/Working/Procedural（不修改则留空）")] string? type = null,
        [Description("新的知识层级: ProjectVision/DisciplineStandard/CrossDiscipline/FeatureSystem/Implementation（不修改则留空）")] string? layer = null,
        [Description("新的标签，逗号分隔（会替换原有标签；不修改则留空）")] string? tags = null,
        [Description("新的关联职能域，逗号分隔（不修改则留空）")] string? disciplines = null,
        [Description("新的重要度 0.0-1.0（不修改则留空）")] double? importance = null,
        [Description("项目根目录（留空自动使用当前项目）")] string? projectRoot = null)
    {
        logger.LogInformation(LogEvents.Mcp, "update_memory() id={Id}", id);
        EnsureKnowledge(projectRoot);

        var existing = memory.GetMemoryById(id);
        if (existing == null)
            return $"错误：记忆不存在 [{id}]";

        var mt = existing.Type;
        var kl = existing.Layer;
        if (!string.IsNullOrWhiteSpace(type) && !Enum.TryParse(type, true, out mt))
            return $"错误：无效的记忆类型 '{type}'";
        if (!string.IsNullOrWhiteSpace(layer) && !Enum.TryParse(layer, true, out kl))
            return $"错误：无效的知识层级 '{layer}'";

        var request = new RememberRequest
        {
            Content = content ?? existing.Content,
            Type = mt,
            Layer = kl,
            Source = existing.Source,
            Summary = summary ?? existing.Summary,
            Disciplines = disciplines != null ? SplitCsv(disciplines) : existing.Disciplines,
            Features = existing.Features,
            NodeId = existing.NodeId,
            Tags = tags != null ? SplitCsv(tags) : existing.Tags,
            ParentId = existing.ParentId,
            Importance = importance ?? existing.Importance
        };

        var updated = await memory.UpdateMemoryAsync(id, request);
        return $"✓ 记忆 [{id}] 已更新\n类型: {updated.Type} | 层级: {updated.Layer}\n摘要: {updated.Summary}";
    }

    [McpServerTool, Description(
        "删除一条项目记忆。用于：错误知识、重复条目、已完全过时不再需要归档的记忆。" +
        "删除后同步清理全文索引和向量索引。此操作不可恢复。")]
    public string delete_memory(
        [Description("要删除的记忆 ID")] string id,
        [Description("项目根目录（留空自动使用当前项目）")] string? projectRoot = null)
    {
        logger.LogInformation(LogEvents.Mcp, "delete_memory() id={Id}", id);
        EnsureKnowledge(projectRoot);

        var existing = memory.GetMemoryById(id);
        if (existing == null)
            return $"错误：记忆不存在 [{id}]";

        var deleted = memory.DeleteMemory(id);
        return deleted
            ? $"✓ 记忆 [{id}] 已删除（{existing.Summary ?? "无摘要"}）"
            : $"错误：删除失败 [{id}]";
    }

    [McpServerTool, Description(
        "按条件筛选项目记忆列表。与 recall 的区别：recall 是语义搜索（输入自然语言问题），" +
        "query_memories 是结构化过滤（按层级/类型/职能/标签等精确筛选）。" +
        "适用于：浏览某层级的全部知识、查看某模块关联的所有记忆、统计某类标签的条目数。")]
    public string query_memories(
        [Description("知识层级过滤，逗号分隔: ProjectVision/DisciplineStandard/CrossDiscipline/FeatureSystem/Implementation")] string? layers = null,
        [Description("记忆类型过滤，逗号分隔: Structural/Semantic/Episodic/Working/Procedural")] string? types = null,
        [Description("职能域过滤，逗号分隔: engineering/design/art/ta/audio/devops/qa")] string? disciplines = null,
        [Description("业务系统过滤，逗号分隔")] string? features = null,
        [Description("节点 ID 过滤")] string? nodeId = null,
        [Description("标签过滤，逗号分隔")] string? tags = null,
        [Description("鲜活度过滤: FreshOnly/FreshAndAging/IncludeStale/IncludeArchived/All（默认 FreshAndAging）")] string? freshness = null,
        [Description("最多返回条数（默认 20，上限 100）")] int limit = 20,
        [Description("项目根目录（留空自动使用当前项目）")] string? projectRoot = null)
    {
        logger.LogInformation(LogEvents.Mcp, "query_memories()");
        EnsureKnowledge(projectRoot);

        if (!Enum.TryParse<FreshnessFilter>(freshness ?? "FreshAndAging", true, out var ff))
            ff = FreshnessFilter.FreshAndAging;

        var filter = new MemoryFilter
        {
            Layers = ParseLayers(layers),
            Types = ParseTypes(types),
            Disciplines = SplitCsvOrNull(disciplines),
            Features = SplitCsvOrNull(features),
            NodeId = nodeId,
            Tags = SplitCsvOrNull(tags),
            Freshness = ff,
            Limit = Math.Clamp(limit, 1, 100)
        };

        var entries = memory.QueryMemories(filter);

        if (entries.Count == 0)
            return "未找到匹配的记忆条目。";

        var sb = new StringBuilder();
        sb.AppendLine($"共 {entries.Count} 条匹配记忆：\n");
        foreach (var e in entries)
        {
            sb.AppendLine($"- [{e.Id}] {e.Summary ?? "(无摘要)"}");
            sb.AppendLine($"  {e.Type}/{e.Layer} | 鲜活度: {e.Freshness} | 重要度: {e.Importance:F1}");
            if (e.Tags.Count > 0)
                sb.AppendLine($"  标签: {string.Join(", ", e.Tags)}");
        }
        return sb.ToString();
    }

    [McpServerTool, Description(
        "按 ID 获取一条记忆的完整内容。返回记忆的所有字段：正文、摘要、类型、层级、标签、关联模块、鲜活度等。" +
        "适用于：查看 recall/query 返回的某条记忆的完整正文、检查记忆详情后决定是否 update 或 delete。")]
    public string get_memory(
        [Description("记忆 ID")] string id,
        [Description("项目根目录（留空自动使用当前项目）")] string? projectRoot = null)
    {
        logger.LogInformation(LogEvents.Mcp, "get_memory() id={Id}", id);
        EnsureKnowledge(projectRoot);

        var entry = memory.GetMemoryById(id);
        if (entry == null)
            return $"错误：记忆不存在 [{id}]";

        return JsonSerializer.Serialize(entry, JsonOpts);
    }

    [McpServerTool, Description(
        "获取知识库整体统计信息。返回各维度的条目计数：按知识层级、按记忆类型、按职能域、按业务系统、按鲜活度。" +
        "适用于：灌入知识后确认效果、评估知识库完整度和健康状况、发现哪些层级/职能的知识缺失。")]
    public string get_memory_stats(
        [Description("项目根目录（留空自动使用当前项目）")] string? projectRoot = null)
    {
        logger.LogInformation(LogEvents.Mcp, "get_memory_stats()");
        EnsureKnowledge(projectRoot);
        var stats = memory.GetMemoryStats();
        return JsonSerializer.Serialize(stats, JsonOpts);
    }

    // ═══════════════════════════════════════════
    //  格式化
    // ═══════════════════════════════════════════

    private static string FormatRecallResult(RecallResult result)
    {
        var sb = new StringBuilder();

        if (result.IsVectorDegraded)
            sb.AppendLine("> ⚠ 向量搜索不可用，已降级到全文检索模式\n");

        if (result.ConstraintChain.Count > 0)
        {
            sb.AppendLine("## 约束链（上层规范）\n");
            foreach (var c in result.ConstraintChain)
            {
                sb.AppendLine($"### [{c.Layer}] {c.Summary ?? "(无摘要)"}");
                sb.AppendLine($"- ID: `{c.Id}` | 类型: {c.Type} | 鲜活度: {c.Freshness}");
                sb.AppendLine($"- {TruncateContent(c.Content, 200)}");
                sb.AppendLine();
            }
            sb.AppendLine("---\n");
        }

        if (result.Memories.Count == 0)
        {
            sb.AppendLine("未找到相关记忆。可以尝试：");
            sb.AppendLine("- 换个问法重新 recall");
            sb.AppendLine("- 用 remember 写入相关知识");
            return sb.ToString();
        }

        sb.AppendLine($"## 检索结果（{result.Memories.Count} 条，置信度 {result.Confidence:F2}）\n");

        for (var i = 0; i < result.Memories.Count; i++)
        {
            var m = result.Memories[i];
            sb.AppendLine($"### {i + 1}. {m.Entry.Summary ?? "(无摘要)"}");
            sb.AppendLine($"- ID: `{m.Entry.Id}` | 通道: {m.MatchChannel} | 分数: {m.Score:F3}");
            sb.AppendLine($"- {m.Entry.Type}/{m.Entry.Layer} | 鲜活度: {m.Entry.Freshness} | 重要度: {m.Entry.Importance:F1}");
            if (m.Entry.Tags.Count > 0)
                sb.AppendLine($"- 标签: {string.Join(", ", m.Entry.Tags)}");
            sb.AppendLine($"- {TruncateContent(m.Entry.Content, 300)}");
            sb.AppendLine();
        }

        if (result.SuggestedFollowUps.Count > 0)
        {
            sb.AppendLine("---");
            sb.AppendLine("**建议深入查看：**");
            foreach (var s in result.SuggestedFollowUps)
                sb.AppendLine($"- {s}");
        }

        return sb.ToString();
    }

    private static string FormatFeatureSummary(FeatureKnowledgeSummary summary)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {summary.FeatureId} 业务系统知识汇总（{summary.TotalCount} 条）\n");

        if (summary.CrossDiscipline.Count > 0)
        {
            sb.AppendLine("## 跨职能协议\n");
            foreach (var entry in summary.CrossDiscipline)
                sb.AppendLine($"- [{entry.Id}] {entry.Summary}");
            sb.AppendLine();
        }

        foreach (var (discipline, entries) in summary.ByDiscipline.OrderBy(kv => kv.Key))
        {
            sb.AppendLine($"## {discipline}（{entries.Count} 条）\n");
            foreach (var entry in entries.OrderByDescending(e => e.Importance).Take(10))
                sb.AppendLine($"- [{entry.Layer}] {entry.Summary} (重要度:{entry.Importance:F1})");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    // ═══════════════════════════════════════════
    //  工具方法
    // ═══════════════════════════════════════════

    private static List<string> SplitCsv(string csv) =>
        csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

    private static List<string>? SplitCsvOrNull(string? csv) =>
        string.IsNullOrWhiteSpace(csv) ? null : SplitCsv(csv);

    private static List<KnowledgeLayer>? ParseLayers(string? layers)
    {
        if (string.IsNullOrWhiteSpace(layers)) return null;
        return SplitCsv(layers)
            .Select(l => Enum.TryParse<KnowledgeLayer>(l, true, out var v) ? v : (KnowledgeLayer?)null)
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .ToList();
    }

    private static List<MemoryType>? ParseTypes(string? types)
    {
        if (string.IsNullOrWhiteSpace(types)) return null;
        return SplitCsv(types)
            .Select(t => Enum.TryParse<MemoryType>(t, true, out var v) ? v : (MemoryType?)null)
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .ToList();
    }

    // ═══════════════════════════════════════════
    //  索引重建
    // ═══════════════════════════════════════════

    [McpServerTool, Description(
        "全量重建记忆索引：清空 SQLite 索引库，从 entries/*.json 文件重新导入全部记忆。" +
        "适用于：git pull 后 JSON 大量变更、索引疑似损坏、手动编辑了 JSON 文件后需要刷新索引。" +
        "操作会清空现有索引（包括向量和 FTS），然后逐个读取 JSON 文件重建。向量嵌入需后续 recall 时按需重新生成。" +
        "设置 rewriteJson=true 时会同时用当前格式重写每个 JSON 文件（修复 Unicode 转义为明文中文等格式问题）。")]
    public string rebuild_index(
        [Description("是否同时重写 JSON 文件（修复 Unicode 转义等格式问题）")] bool rewriteJson = false,
        [Description("项目根目录（留空自动使用当前项目）")] string? projectRoot = null)
    {
        logger.LogInformation(LogEvents.Mcp, "rebuild_index(rewriteJson={Rewrite})", rewriteJson);
        EnsureKnowledge(projectRoot);

        try
        {
            var (imported, skipped) = memory.RebuildIndex(rewriteJson);
            var extra = rewriteJson ? "\nJSON 文件已重写为明文格式。" : "";
            return $"✓ 全量重建完成\n- 导入: {imported} 条\n- 跳过: {skipped} 条{extra}\n\n索引已刷新，向量嵌入将在下次 recall 时按需重新生成。";
        }
        catch (Exception ex)
        {
            return $"错误：重建索引失败 — {ex.Message}";
        }
    }

    [McpServerTool, Description(
        "增量同步记忆索引：将 entries/*.json 中新增的文件补入索引，同时清理索引中指向已删除 JSON 的孤儿记录。" +
        "适用于：git pull 后有少量新增或删除的 JSON 文件，不想全量重建时使用。" +
        "比 rebuild_index 更快，不清空已有索引，只做差异补齐。")]
    public string sync_index(
        [Description("项目根目录（留空自动使用当前项目）")] string? projectRoot = null)
    {
        logger.LogInformation(LogEvents.Mcp, "sync_index()");
        EnsureKnowledge(projectRoot);

        try
        {
            var (added, removed, skipped) = memory.SyncFromJson();
            return $"✓ 增量同步完成\n- 新增: {added} 条\n- 移除孤儿: {removed} 条\n- 跳过: {skipped} 条";
        }
        catch (Exception ex)
        {
            return $"错误：增量同步失败 — {ex.Message}";
        }
    }

    [McpServerTool, Description(
        "从索引反写 JSON：将 SQLite 中的全部记忆导出为 entries/*.json 文件。" +
        "适用于：JSON 文件损坏或丢失后从索引恢复、修复 Unicode 转义为明文中文。" +
        "已有的 JSON 文件会被覆盖为当前格式（明文中文）。不影响索引数据。")]
    public string export_to_json(
        [Description("项目根目录（留空自动使用当前项目）")] string? projectRoot = null)
    {
        logger.LogInformation(LogEvents.Mcp, "export_to_json()");
        EnsureKnowledge(projectRoot);

        try
        {
            var (exported, skipped) = memory.ExportToJson();
            return $"✓ 导出完成\n- 导出: {exported} 条\n- 跳过: {skipped} 条\n\nJSON 文件已重写为明文格式。";
        }
        catch (Exception ex)
        {
            return $"错误：导出失败 — {ex.Message}";
        }
    }

    // ═══════════════════════════════════════════
    //  格式化
    // ═══════════════════════════════════════════

    private static string TruncateContent(string content, int maxLen)
    {
        var clean = content.Replace('\n', ' ').Replace('\r', ' ');
        return clean.Length <= maxLen ? clean : clean[..maxLen] + "…";
    }

    private void EnsureKnowledge(string? projectRoot)
    {
        var root = config.Resolve(projectRoot);
        var store = config.ResolveStore(null, root);
        memory.Initialize(root, store);
    }

    private sealed class BatchEntry
    {
        public string Content { get; set; } = "";
        public string Type { get; set; } = "";
        public string Layer { get; set; } = "";
        public string? Disciplines { get; set; }
        public string? Tags { get; set; }
        public string? Summary { get; set; }
        public string? Features { get; set; }
        public string? NodeId { get; set; }
        public string? ParentId { get; set; }
        public double? Importance { get; set; }
    }
}
