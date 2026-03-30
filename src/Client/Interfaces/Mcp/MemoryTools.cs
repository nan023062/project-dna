using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Dna.Client.Services;
using Dna.Knowledge;
using Dna.Memory.Models;
using ModelContextProtocol.Server;

namespace Dna.Client.Interfaces.Mcp;

[McpServerToolType]
public sealed class MemoryTools(DnaServerApi api)
{
    private static readonly JsonSerializerOptions PrettyJson = new() { WriteIndented = true };

    [McpServerTool, Description("写入一条项目记忆。")]
    public async Task<string> remember(
        [Description("知识正文（文本或 JSON）")] string content,
        [Description("记忆类型: Structural/Semantic/Episodic/Working/Procedural")] string type,
        [Description("关联职能域，逗号分隔")] string disciplines,
        [Description("节点类型: Project/Department/Technical/Team")] string? nodeType = null,
        [Description("兼容旧参数，已废弃")] string? layer = null,
        [Description("标签，逗号分隔")] string? tags = null,
        [Description("一句话摘要")] string? summary = null,
        [Description("关联业务系统，逗号分隔")] string? features = null,
        [Description("所属节点 ID")] string? nodeId = null,
        [Description("上层约束记忆 ID")] string? parentId = null,
        [Description("重要度 0.0-1.0")] double importance = 0.5)
    {
        try
        {
            if (!Enum.TryParse<MemoryType>(type, true, out var memoryType))
                return $"错误：无效的记忆类型 '{type}'。";
            if (!NodeTypeCompat.TryParse(nodeType ?? layer, out var parsedNodeType))
                return $"错误：无效的节点类型 '{nodeType ?? layer}'。";

            var payload = new RememberRequest
            {
                Content = content,
                Type = memoryType,
                NodeType = parsedNodeType,
                Source = MemorySource.Ai,
                Summary = summary,
                Disciplines = SplitCsv(disciplines),
                Features = SplitCsvOrNull(features),
                NodeId = nodeId,
                Tags = SplitCsv(tags),
                ParentId = parentId,
                Importance = Math.Clamp(importance, 0, 1)
            };

            var result = await api.PostAsync("/api/memory/remember", payload);
            return $"✓ 记忆已写入 [{GetString(result, "id")}]";
        }
        catch (Exception ex)
        {
            return $"错误：{ex.Message}";
        }
    }

    [McpServerTool, Description("语义检索项目记忆。")]
    public async Task<string> recall(
        [Description("自然语言问题")] string question,
        [Description("限定职能域，逗号分隔")] string? disciplines = null,
        [Description("限定业务系统，逗号分隔")] string? features = null,
        [Description("限定节点 ID")] string? nodeId = null,
        [Description("精确匹配标签，逗号分隔")] string? tags = null,
        [Description("限定节点类型，逗号分隔: Project/Department/Technical/Team")] string? nodeTypes = null,
        [Description("兼容旧参数，已废弃")] string? layers = null,
        [Description("是否展开约束链")] bool expandChain = true,
        [Description("最多返回条数")] int maxResults = 10)
    {
        try
        {
            var payload = new RecallQuery
            {
                Question = question,
                Disciplines = SplitCsvOrNull(disciplines),
                Features = SplitCsvOrNull(features),
                NodeId = nodeId,
                Tags = SplitCsvOrNull(tags),
                NodeTypes = ParseNodeTypes(nodeTypes, layers),
                ExpandConstraintChain = expandChain,
                MaxResults = Math.Clamp(maxResults, 1, 50)
            };

            var result = await api.PostAsync("/api/memory/recall", payload);
            return FormatRecallResult(result);
        }
        catch (Exception ex)
        {
            return $"错误：{ex.Message}";
        }
    }

    [McpServerTool, Description("确认一条记忆仍然有效。")]
    public async Task<string> verify_memory([Description("记忆 ID")] string memoryId)
    {
        try
        {
            var result = await api.PostAsync($"/api/memory/{Uri.EscapeDataString(memoryId)}/verify", new { });
            return JsonSerializer.Serialize(result, PrettyJson);
        }
        catch (Exception ex)
        {
            return $"错误：{ex.Message}";
        }
    }

    [McpServerTool, Description("获取业务系统（Feature）的全职能知识汇总。")]
    public async Task<string> get_feature_summary([Description("业务系统 ID")] string featureId)
    {
        try
        {
            var result = await api.GetAsync($"/api/memory/feature/{Uri.EscapeDataString(featureId)}");
            return JsonSerializer.Serialize(result, PrettyJson);
        }
        catch (Exception ex)
        {
            return $"错误：{ex.Message}";
        }
    }

    [McpServerTool, Description("批量写入多条项目记忆。")]
    public async Task<string> batch_remember([Description("JSON 数组")] string entries)
    {
        try
        {
            var items = JsonSerializer.Deserialize<List<BatchEntry>>(entries);
            if (items == null || items.Count == 0) return "错误：entries 为空。";
            if (items.Count > 50) return $"错误：单次最多 50 条，当前 {items.Count} 条。";

            var requests = new List<RememberRequest>();
            var errors = new List<string>();
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (string.IsNullOrWhiteSpace(item.Content))
                {
                    errors.Add($"[{i}] content 为空");
                    continue;
                }

                if (!Enum.TryParse<MemoryType>(item.Type, true, out var mt))
                {
                    errors.Add($"[{i}] 无效 type '{item.Type}'");
                    continue;
                }

                if (!NodeTypeCompat.TryParse(item.NodeType ?? item.Layer, out var nt))
                {
                    errors.Add($"[{i}] 无效 nodeType/layer '{item.NodeType ?? item.Layer}'");
                    continue;
                }

                requests.Add(new RememberRequest
                {
                    Content = item.Content,
                    Type = mt,
                    NodeType = nt,
                    Source = MemorySource.Ai,
                    Summary = item.Summary,
                    Disciplines = SplitCsv(item.Disciplines),
                    Features = SplitCsvOrNull(item.Features),
                    NodeId = item.NodeId,
                    Tags = SplitCsv(item.Tags),
                    ParentId = item.ParentId,
                    Importance = Math.Clamp(item.Importance ?? 0.5, 0, 1)
                });
            }

            if (requests.Count == 0)
                return $"错误：全部条目校验失败\n{string.Join("\n", errors)}";

            var result = await api.PostAsync("/api/memory/batch", requests);
            var sb = new StringBuilder();
            sb.AppendLine($"✓ 批量写入完成：成功 {requests.Count} 条，失败 {errors.Count} 条");
            if (errors.Count > 0)
            {
                sb.AppendLine("失败详情：");
                foreach (var error in errors) sb.AppendLine($"- {error}");
            }
            sb.AppendLine(JsonSerializer.Serialize(result, PrettyJson));
            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"错误：{ex.Message}";
        }
    }

    [McpServerTool, Description("修改一条已有的项目记忆。")]
    public async Task<string> update_memory(
        [Description("要修改的记忆 ID")] string id,
        [Description("新的知识正文（不修改则留空）")] string? content = null,
        [Description("新的摘要（不修改则留空）")] string? summary = null,
        [Description("新的记忆类型（不修改则留空）")] string? type = null,
        [Description("新的节点类型（不修改则留空）")] string? nodeType = null,
        [Description("兼容旧参数，已废弃")] string? layer = null,
        [Description("新的标签，逗号分隔（会替换原有标签；不修改则留空）")] string? tags = null,
        [Description("新的关联职能域，逗号分隔（不修改则留空）")] string? disciplines = null,
        [Description("新的重要度 0.0-1.0（不修改则留空）")] double? importance = null)
    {
        try
        {
            var existing = await api.GetAsync($"/api/memory/{Uri.EscapeDataString(id)}");
            if (existing.ValueKind != JsonValueKind.Object) return $"错误：记忆不存在 [{id}]";

            var typeValue = ParseMemoryType(existing);
            var nodeTypeValue = ParseNodeType(existing);
            if (!string.IsNullOrWhiteSpace(type) && !Enum.TryParse<MemoryType>(type, true, out typeValue))
                return $"错误：无效的记忆类型 '{type}'";
            if (!string.IsNullOrWhiteSpace(nodeType) || !string.IsNullOrWhiteSpace(layer))
            {
                if (!NodeTypeCompat.TryParse(nodeType ?? layer, out nodeTypeValue))
                    return $"错误：无效的节点类型 '{nodeType ?? layer}'";
            }

            var payload = new RememberRequest
            {
                Content = content ?? GetString(existing, "content") ?? string.Empty,
                Type = typeValue,
                NodeType = nodeTypeValue,
                Source = ParseMemorySource(existing),
                Summary = summary ?? GetString(existing, "summary"),
                Disciplines = disciplines != null ? SplitCsv(disciplines) : GetStringList(existing, "disciplines"),
                Features = GetStringList(existing, "features"),
                NodeId = GetString(existing, "nodeId"),
                Tags = tags != null ? SplitCsv(tags) : GetStringList(existing, "tags"),
                ParentId = GetString(existing, "parentId"),
                Importance = importance ?? GetDouble(existing, "importance", 0.5)
            };

            var result = await api.PutAsync($"/api/memory/{Uri.EscapeDataString(id)}", payload);
            return $"✓ 记忆 [{id}] 已更新\n{JsonSerializer.Serialize(result, PrettyJson)}";
        }
        catch (Exception ex)
        {
            return $"错误：{ex.Message}";
        }
    }

    [McpServerTool, Description("删除一条项目记忆。")]
    public async Task<string> delete_memory([Description("要删除的记忆 ID")] string id)
    {
        try
        {
            var result = await api.DeleteAsync($"/api/memory/{Uri.EscapeDataString(id)}");
            return JsonSerializer.Serialize(result, PrettyJson);
        }
        catch (Exception ex)
        {
            return $"错误：{ex.Message}";
        }
    }

    [McpServerTool, Description("按条件筛选项目记忆列表。")]
    public async Task<string> query_memories(
        [Description("节点类型过滤，逗号分隔")] string? nodeTypes = null,
        [Description("兼容旧参数，已废弃")] string? layers = null,
        [Description("记忆类型过滤，逗号分隔")] string? types = null,
        [Description("职能域过滤，逗号分隔")] string? disciplines = null,
        [Description("业务系统过滤，逗号分隔")] string? features = null,
        [Description("节点 ID 过滤")] string? nodeId = null,
        [Description("标签过滤，逗号分隔")] string? tags = null,
        [Description("鲜活度过滤")] string? freshness = null,
        [Description("最多返回条数（默认 20，上限 100）")] int limit = 20)
    {
        try
        {
            var parts = new List<string>
            {
                $"limit={Math.Clamp(limit, 1, 100)}"
            };
            if (!string.IsNullOrWhiteSpace(nodeTypes)) parts.Add($"nodeTypes={Uri.EscapeDataString(nodeTypes)}");
            if (!string.IsNullOrWhiteSpace(layers)) parts.Add($"layers={Uri.EscapeDataString(layers)}");
            if (!string.IsNullOrWhiteSpace(types)) parts.Add($"types={Uri.EscapeDataString(types)}");
            if (!string.IsNullOrWhiteSpace(disciplines)) parts.Add($"disciplines={Uri.EscapeDataString(disciplines)}");
            if (!string.IsNullOrWhiteSpace(features)) parts.Add($"features={Uri.EscapeDataString(features)}");
            if (!string.IsNullOrWhiteSpace(nodeId)) parts.Add($"nodeId={Uri.EscapeDataString(nodeId)}");
            if (!string.IsNullOrWhiteSpace(tags)) parts.Add($"tags={Uri.EscapeDataString(tags)}");
            if (!string.IsNullOrWhiteSpace(freshness)) parts.Add($"freshness={Uri.EscapeDataString(freshness)}");

            var result = await api.GetAsync($"/api/memory/query?{string.Join("&", parts)}");
            if (result.ValueKind != JsonValueKind.Array || result.GetArrayLength() == 0)
                return "未找到匹配的记忆条目。";

            var sb = new StringBuilder();
            sb.AppendLine($"共 {result.GetArrayLength()} 条匹配记忆：");
            foreach (var item in result.EnumerateArray())
            {
                sb.AppendLine($"- [{GetString(item, "id")}] {GetString(item, "summary") ?? "(无摘要)"}");
            }
            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"错误：{ex.Message}";
        }
    }

    [McpServerTool, Description("按 ID 获取一条记忆的完整内容。")]
    public async Task<string> get_memory([Description("记忆 ID")] string id)
    {
        try
        {
            var result = await api.GetAsync($"/api/memory/{Uri.EscapeDataString(id)}");
            return JsonSerializer.Serialize(result, PrettyJson);
        }
        catch (Exception ex)
        {
            return $"错误：{ex.Message}";
        }
    }

    [McpServerTool, Description("获取知识库整体统计信息。")]
    public async Task<string> get_memory_stats()
    {
        try
        {
            var result = await api.GetAsync("/api/memory/stats");
            return JsonSerializer.Serialize(result, PrettyJson);
        }
        catch (Exception ex)
        {
            return $"错误：{ex.Message}";
        }
    }

    [McpServerTool, Description("兼容旧命名：重建记忆搜索索引（FTS）。")]
    public async Task<string> import_from_json([Description("保留参数，无实际作用")] bool rewriteJson = false)
    {
        try
        {
            var result = await api.PostAsync($"/api/memory/index/rebuild?rewriteJson={rewriteJson.ToString().ToLowerInvariant()}", new { });
            return JsonSerializer.Serialize(result, PrettyJson);
        }
        catch (Exception ex)
        {
            return $"错误：{ex.Message}";
        }
    }

    [McpServerTool, Description("兼容旧命名：执行索引刷新。")]
    public async Task<string> import_new_from_json()
    {
        try
        {
            var result = await api.PostAsync("/api/memory/index/sync", new { });
            return JsonSerializer.Serialize(result, PrettyJson);
        }
        catch (Exception ex)
        {
            return $"错误：{ex.Message}";
        }
    }

    [McpServerTool, Description("兼容旧命名：纯 DB 存储下不再导出记忆 JSON。")]
    public async Task<string> export_to_json()
    {
        try
        {
            var result = await api.PostAsync("/api/memory/index/export", new { });
            return JsonSerializer.Serialize(result, PrettyJson);
        }
        catch (Exception ex)
        {
            return $"错误：{ex.Message}";
        }
    }

    private static string FormatRecallResult(JsonElement result)
    {
        var confidence = GetDouble(result, "confidence", 0);
        var sb = new StringBuilder();
        var memories = result.TryGetProperty("memories", out var mArr) && mArr.ValueKind == JsonValueKind.Array
            ? mArr.EnumerateArray().ToList()
            : [];
        var chain = result.TryGetProperty("constraintChain", out var cArr) && cArr.ValueKind == JsonValueKind.Array
            ? cArr.EnumerateArray().ToList()
            : [];

        if (chain.Count > 0)
        {
            sb.AppendLine("## 约束链");
            foreach (var item in chain)
                sb.AppendLine($"- {GetString(item, "summary") ?? "(无摘要)"}");
            sb.AppendLine();
        }

        if (memories.Count == 0)
        {
            sb.AppendLine("未找到相关记忆。");
            return sb.ToString();
        }

        sb.AppendLine($"## 检索结果（{memories.Count} 条，置信度 {confidence:F2}）");
        sb.AppendLine();
        foreach (var scored in memories)
        {
            var entry = scored.TryGetProperty("entry", out var e) ? e : default;
            sb.AppendLine($"- [{GetString(entry, "id")}] {GetString(entry, "summary") ?? "(无摘要)"}");
        }
        return sb.ToString();
    }

    private static List<string> SplitCsv(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? []
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static List<string>? SplitCsvOrNull(string? csv)
    {
        var list = SplitCsv(csv);
        return list.Count > 0 ? list : null;
    }

    private static List<NodeType>? ParseNodeTypes(string? nodeTypes, string? legacyLayers = null)
    {
        var merged = new List<string>();
        if (!string.IsNullOrWhiteSpace(nodeTypes)) merged.AddRange(SplitCsv(nodeTypes));
        if (!string.IsNullOrWhiteSpace(legacyLayers)) merged.AddRange(SplitCsv(legacyLayers));
        if (merged.Count == 0) return null;

        return merged
            .Select(value => NodeTypeCompat.TryParse(value, out var parsed) ? (NodeType?)parsed : null)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .Distinct()
            .ToList();
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        if (!element.TryGetProperty(propertyName, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static double GetDouble(JsonElement element, string propertyName, double fallback)
    {
        if (element.ValueKind != JsonValueKind.Object) return fallback;
        if (!element.TryGetProperty(propertyName, out var value)) return fallback;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)) return number;
        return fallback;
    }

    private static List<string> GetStringList(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object) return [];
        if (!element.TryGetProperty(propertyName, out var arr) || arr.ValueKind != JsonValueKind.Array) return [];
        return arr.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString() ?? string.Empty)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();
    }

    private static MemoryType ParseMemoryType(JsonElement entry)
    {
        if (entry.TryGetProperty("type", out var type))
        {
            if (type.ValueKind == JsonValueKind.Number && type.TryGetInt32(out var n) && Enum.IsDefined(typeof(MemoryType), n))
                return (MemoryType)n;
            if (type.ValueKind == JsonValueKind.String && Enum.TryParse<MemoryType>(type.GetString(), true, out var parsed))
                return parsed;
        }
        return MemoryType.Semantic;
    }

    private static MemorySource ParseMemorySource(JsonElement entry)
    {
        if (entry.TryGetProperty("source", out var source))
        {
            if (source.ValueKind == JsonValueKind.Number && source.TryGetInt32(out var n) && Enum.IsDefined(typeof(MemorySource), n))
                return (MemorySource)n;
            if (source.ValueKind == JsonValueKind.String && Enum.TryParse<MemorySource>(source.GetString(), true, out var parsed))
                return parsed;
        }
        return MemorySource.Ai;
    }

    private static NodeType ParseNodeType(JsonElement entry)
    {
        if (entry.TryGetProperty("nodeType", out var nodeType))
        {
            if (nodeType.ValueKind == JsonValueKind.Number && nodeType.TryGetInt32(out var n) && Enum.IsDefined(typeof(NodeType), n))
                return (NodeType)n;
            if (nodeType.ValueKind == JsonValueKind.String && NodeTypeCompat.TryParse(nodeType.GetString(), out var parsed))
                return parsed;
        }
        return NodeType.Technical;
    }

    private sealed class BatchEntry
    {
        public string Content { get; set; } = "";
        public string Type { get; set; } = "";
        public string? NodeType { get; set; }
        public string? Layer { get; set; }
        public string? Disciplines { get; set; }
        public string? Tags { get; set; }
        public string? Summary { get; set; }
        public string? Features { get; set; }
        public string? NodeId { get; set; }
        public string? ParentId { get; set; }
        public double? Importance { get; set; }
    }
}
