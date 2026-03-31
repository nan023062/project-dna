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

    [McpServerTool, Description("Create a new memory entry directly.")]
    public async Task<string> remember(
        [Description("Memory content in text or JSON format.")] string content,
        [Description("Memory type: Structural/Semantic/Episodic/Working/Procedural")] string type,
        [Description("Disciplines, comma separated.")] string disciplines,
        [Description("Node type: Project/Department/Technical/Team")] string? nodeType = null,
        [Description("Legacy alias for node type.")] string? layer = null,
        [Description("Tags, comma separated.")] string? tags = null,
        [Description("Short summary.")] string? summary = null,
        [Description("Features, comma separated.")] string? features = null,
        [Description("Related node id.")] string? nodeId = null,
        [Description("Parent memory id.")] string? parentId = null,
        [Description("Importance between 0.0 and 1.0.")] double importance = 0.5)
    {
        try
        {
            if (!Enum.TryParse<MemoryType>(type, true, out var memoryType))
                return $"Error: invalid memory type '{type}'.";
            if (!NodeTypeCompat.TryParse(nodeType ?? layer, out var parsedNodeType))
                return $"Error: invalid node type '{nodeType ?? layer}'.";

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
            return $"Memory created [{GetString(result, "id")}].";
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool, Description("Recall memories semantically.")]
    public async Task<string> recall(
        [Description("Natural language question.")] string question,
        [Description("Disciplines, comma separated.")] string? disciplines = null,
        [Description("Features, comma separated.")] string? features = null,
        [Description("Node id filter.")] string? nodeId = null,
        [Description("Tags, comma separated.")] string? tags = null,
        [Description("Node types, comma separated: Project/Department/Technical/Team")] string? nodeTypes = null,
        [Description("Legacy alias for node types.")] string? layers = null,
        [Description("Expand constraint chain.")] bool expandChain = true,
        [Description("Max results.")] int maxResults = 10)
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
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool, Description("Verify that a memory is still valid.")]
    public async Task<string> verify_memory([Description("Memory id.")] string memoryId)
    {
        try
        {
            var result = await api.PostAsync($"/api/memory/{Uri.EscapeDataString(memoryId)}/verify", new { });
            return JsonSerializer.Serialize(result, PrettyJson);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool, Description("Get the summary of a feature.")]
    public async Task<string> get_feature_summary([Description("Feature id.")] string featureId)
    {
        try
        {
            var result = await api.GetAsync($"/api/memory/feature/{Uri.EscapeDataString(featureId)}");
            return JsonSerializer.Serialize(result, PrettyJson);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool, Description("Create multiple new memories directly.")]
    public async Task<string> batch_remember([Description("JSON array of entries.")] string entries)
    {
        try
        {
            var items = JsonSerializer.Deserialize<List<BatchEntry>>(entries);
            if (items == null || items.Count == 0) return "Error: entries is empty.";
            if (items.Count > 50) return $"Error: a single batch supports at most 50 entries, current count is {items.Count}.";

            var requests = new List<RememberRequest>();
            var errors = new List<string>();
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (string.IsNullOrWhiteSpace(item.Content))
                {
                    errors.Add($"[{i}] content is empty");
                    continue;
                }

                if (!Enum.TryParse<MemoryType>(item.Type, true, out var memoryType))
                {
                    errors.Add($"[{i}] invalid type '{item.Type}'");
                    continue;
                }

                if (!NodeTypeCompat.TryParse(item.NodeType ?? item.Layer, out var parsedNodeType))
                {
                    errors.Add($"[{i}] invalid nodeType/layer '{item.NodeType ?? item.Layer}'");
                    continue;
                }

                requests.Add(new RememberRequest
                {
                    Content = item.Content,
                    Type = memoryType,
                    NodeType = parsedNodeType,
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
                return $"Error: all entries failed validation.\n{string.Join("\n", errors)}";

            var result = await api.PostAsync("/api/memory/batch", requests);
            var ids = result.TryGetProperty("ids", out var idArray) && idArray.ValueKind == JsonValueKind.Array
                ? idArray.EnumerateArray().Select(item => item.GetString()).Where(value => !string.IsNullOrWhiteSpace(value)).ToList()
                : [];

            var sb = new StringBuilder();
            sb.AppendLine($"Batch memories created: {ids.Count}, validation failures: {errors.Count}.");
            if (errors.Count > 0)
            {
                sb.AppendLine("Validation failures:");
                foreach (var error in errors)
                    sb.AppendLine($"- {error}");
            }
            if (ids.Count > 0)
            {
                sb.AppendLine("Memory ids:");
                foreach (var memoryId in ids)
                    sb.AppendLine($"- {memoryId}");
            }
            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool, Description("Update an existing memory directly.")]
    public async Task<string> update_memory(
        [Description("Target memory id.")] string id,
        [Description("New content, optional.")] string? content = null,
        [Description("New summary, optional.")] string? summary = null,
        [Description("New memory type, optional.")] string? type = null,
        [Description("New node type, optional.")] string? nodeType = null,
        [Description("Legacy alias for node type.")] string? layer = null,
        [Description("Replacement tags, comma separated.")] string? tags = null,
        [Description("Replacement disciplines, comma separated.")] string? disciplines = null,
        [Description("New importance, optional.")] double? importance = null)
    {
        try
        {
            var existing = await api.GetAsync($"/api/memory/{Uri.EscapeDataString(id)}");
            if (existing.ValueKind != JsonValueKind.Object)
                return $"Error: memory [{id}] was not found.";

            var typeValue = ParseMemoryType(existing);
            var nodeTypeValue = ParseNodeType(existing);
            if (!string.IsNullOrWhiteSpace(type) && !Enum.TryParse<MemoryType>(type, true, out typeValue))
                return $"Error: invalid memory type '{type}'.";

            if (!string.IsNullOrWhiteSpace(nodeType) || !string.IsNullOrWhiteSpace(layer))
            {
                if (!NodeTypeCompat.TryParse(nodeType ?? layer, out nodeTypeValue))
                    return $"Error: invalid node type '{nodeType ?? layer}'.";
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
            return $"Memory [{id}] updated ({GetString(result, "summary") ?? "ok"}).";
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool, Description("Delete a memory directly.")]
    public async Task<string> delete_memory([Description("Target memory id.")] string id)
    {
        try
        {
            var result = await api.DeleteAsync($"/api/memory/{Uri.EscapeDataString(id)}");
            return GetString(result, "message") ?? $"Memory [{id}] deleted.";
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool, Description("Query memories by filters.")]
    public async Task<string> query_memories(
        [Description("Node types, comma separated.")] string? nodeTypes = null,
        [Description("Legacy alias for node types.")] string? layers = null,
        [Description("Memory types, comma separated.")] string? types = null,
        [Description("Disciplines, comma separated.")] string? disciplines = null,
        [Description("Features, comma separated.")] string? features = null,
        [Description("Node id filter.")] string? nodeId = null,
        [Description("Tags, comma separated.")] string? tags = null,
        [Description("Freshness filter.")] string? freshness = null,
        [Description("Max results, default 20, up to 100.")] int limit = 20)
    {
        try
        {
            var parts = new List<string> { $"limit={Math.Clamp(limit, 1, 100)}" };
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
                return "No matching memories were found.";

            var sb = new StringBuilder();
            sb.AppendLine($"Matched memories: {result.GetArrayLength()}");
            foreach (var item in result.EnumerateArray())
                sb.AppendLine($"- [{GetString(item, "id")}] {GetString(item, "summary") ?? "(no summary)"}");
            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool, Description("Get a memory by id.")]
    public async Task<string> get_memory([Description("Memory id.")] string id)
    {
        try
        {
            var result = await api.GetAsync($"/api/memory/{Uri.EscapeDataString(id)}");
            return JsonSerializer.Serialize(result, PrettyJson);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool, Description("Get memory statistics.")]
    public async Task<string> get_memory_stats()
    {
        try
        {
            var result = await api.GetAsync("/api/memory/stats");
            return JsonSerializer.Serialize(result, PrettyJson);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool, Description("Legacy alias: rebuild the memory index.")]
    public async Task<string> import_from_json([Description("Legacy flag, ignored.")] bool rewriteJson = false)
    {
        try
        {
            var result = await api.PostAsync($"/api/memory/index/rebuild?rewriteJson={rewriteJson.ToString().ToLowerInvariant()}", new { });
            return JsonSerializer.Serialize(result, PrettyJson);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool, Description("Legacy alias: sync the memory index.")]
    public async Task<string> import_new_from_json()
    {
        try
        {
            var result = await api.PostAsync("/api/memory/index/sync", new { });
            return JsonSerializer.Serialize(result, PrettyJson);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool, Description("Legacy alias: export index data.")]
    public async Task<string> export_to_json()
    {
        try
        {
            var result = await api.PostAsync("/api/memory/index/export", new { });
            return JsonSerializer.Serialize(result, PrettyJson);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    private static string FormatRecallResult(JsonElement result)
    {
        var confidence = GetDouble(result, "confidence", 0);
        var sb = new StringBuilder();
        var memories = result.TryGetProperty("memories", out var memoryArray) && memoryArray.ValueKind == JsonValueKind.Array
            ? memoryArray.EnumerateArray().ToList()
            : [];
        var chain = result.TryGetProperty("constraintChain", out var chainArray) && chainArray.ValueKind == JsonValueKind.Array
            ? chainArray.EnumerateArray().ToList()
            : [];

        if (chain.Count > 0)
        {
            sb.AppendLine("## Constraint Chain");
            foreach (var item in chain)
                sb.AppendLine($"- {GetString(item, "summary") ?? "(no summary)"}");
            sb.AppendLine();
        }

        if (memories.Count == 0)
        {
            sb.AppendLine("No relevant memories were found.");
            return sb.ToString();
        }

        sb.AppendLine($"## Recall Results ({memories.Count}, confidence {confidence:F2})");
        sb.AppendLine();
        foreach (var scored in memories)
        {
            var entry = scored.TryGetProperty("entry", out var item) ? item : default;
            sb.AppendLine($"- [{GetString(entry, "id")}] {GetString(entry, "summary") ?? "(no summary)"}");
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
        if (!element.TryGetProperty(propertyName, out var array) || array.ValueKind != JsonValueKind.Array) return [];
        return array.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString() ?? string.Empty)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();
    }

    private static MemoryType ParseMemoryType(JsonElement entry)
    {
        if (entry.TryGetProperty("type", out var type))
        {
            if (type.ValueKind == JsonValueKind.Number && type.TryGetInt32(out var number) && Enum.IsDefined(typeof(MemoryType), number))
                return (MemoryType)number;
            if (type.ValueKind == JsonValueKind.String && Enum.TryParse<MemoryType>(type.GetString(), true, out var parsed))
                return parsed;
        }

        return MemoryType.Semantic;
    }

    private static MemorySource ParseMemorySource(JsonElement entry)
    {
        if (entry.TryGetProperty("source", out var source))
        {
            if (source.ValueKind == JsonValueKind.Number && source.TryGetInt32(out var number) && Enum.IsDefined(typeof(MemorySource), number))
                return (MemorySource)number;
            if (source.ValueKind == JsonValueKind.String && Enum.TryParse<MemorySource>(source.GetString(), true, out var parsed))
                return parsed;
        }

        return MemorySource.Ai;
    }

    private static NodeType ParseNodeType(JsonElement entry)
    {
        if (entry.TryGetProperty("nodeType", out var nodeType))
        {
            if (nodeType.ValueKind == JsonValueKind.Number && nodeType.TryGetInt32(out var number) && Enum.IsDefined(typeof(NodeType), number))
                return (NodeType)number;
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
