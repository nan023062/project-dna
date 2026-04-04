using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Dna.App.Services;
using Dna.Knowledge;
using ModelContextProtocol.Server;

namespace Dna.App.Interfaces.Mcp;

[McpServerToolType]
public sealed class KnowledgeTools(DnaServerApi api)
{
    private static readonly JsonSerializerOptions PrettyJson = new() { WriteIndented = true };

    [McpServerTool, Description("查看当前拓扑摘要。")]
    public async Task<string> get_topology()
    {
        try
        {
            var topo = await api.GetAsync("/api/topology");
            var summary = GetString(topo, "summary") ?? "Topology loaded.";
            var modules = topo.TryGetProperty("modules", out var modulesArray) && modulesArray.ValueKind == JsonValueKind.Array
                ? modulesArray.GetArrayLength()
                : 0;
            var edges = topo.TryGetProperty("relationEdges", out var relationsArray) && relationsArray.ValueKind == JsonValueKind.Array
                ? relationsArray.GetArrayLength()
                : 0;

            return $"# Topology\n\n- Summary: {summary}\n- Modules: {modules}\n- Relations: {edges}";
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool, Description("导出模块类图描述协议（MCDP）投影视图。")]
    public async Task<string> export_mcdp()
    {
        try
        {
            var result = await api.GetAsync("/api/mcdp");
            return JsonSerializer.Serialize(result, PrettyJson);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool, Description("为一组模块生成依赖顺序。")]
    public async Task<string> get_dependency_order(
        [Description("模块名列表，逗号分隔。")] string moduleNames)
    {
        try
        {
            var value = Uri.EscapeDataString(moduleNames ?? string.Empty);
            var result = await api.GetAsync($"/api/plan?modules={value}");
            var order = result.TryGetProperty("orderedModules", out var array) && array.ValueKind == JsonValueKind.Array
                ? string.Join(" -> ", array.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)))
                : "(empty)";
            var hasCycle = result.TryGetProperty("hasCycle", out var cycleFlag) && cycleFlag.ValueKind == JsonValueKind.True;
            var cycle = GetString(result, "cycleDescription");

            var sb = new StringBuilder();
            sb.AppendLine("## Dependency Order");
            sb.AppendLine();
            sb.AppendLine($"- Order: {order}");
            if (hasCycle)
                sb.AppendLine($"- Cycle: {cycle ?? "Detected"}");
            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool, Description("按关键字搜索模块。")]
    public async Task<string> search_modules(
        [Description("搜索关键词。")] string query,
        [Description("最多返回条数，默认 8。")] int maxResults = 8)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
                return "Error: query must be at least 2 characters.";

            var q = Uri.EscapeDataString(query.Trim());
            var result = await api.GetAsync($"/api/graph/search?q={q}&maxResults={Math.Clamp(maxResults, 1, 20)}");
            if (!result.TryGetProperty("results", out var array) || array.ValueKind != JsonValueKind.Array || array.GetArrayLength() == 0)
                return $"No modules found for '{query}'.";

            var sb = new StringBuilder();
            sb.AppendLine($"## Search Results ({array.GetArrayLength()})");
            sb.AppendLine();
            foreach (var item in array.EnumerateArray())
            {
                sb.AppendLine($"- **{GetString(item, "name") ?? "(unknown)"}** ({GetString(item, "discipline") ?? "generic"})");
                var summary = GetString(item, "summary");
                if (!string.IsNullOrWhiteSpace(summary))
                    sb.AppendLine($"  {summary}");
            }

            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool, Description("获取一个或多个模块的上下文。")]
    public async Task<string> get_context(
        [Description("模块名，支持逗号分隔多个；留空返回模块速查。")] string? moduleNames = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(moduleNames))
            {
                var result = await api.PostAsync("/api/graph/begin-task", new { });
                if (!result.TryGetProperty("modules", out var modules) || modules.ValueKind != JsonValueKind.Array)
                    return "No modules returned.";

                var sb = new StringBuilder();
                sb.AppendLine("# Module Quick View");
                sb.AppendLine();
                foreach (var module in modules.EnumerateArray())
                    sb.AppendLine($"- {GetString(module, "name")} ({GetString(module, "discipline")})");
                sb.AppendLine();
                sb.AppendLine("Tip: call get_context(\"ModuleName\") for detailed context.");
                return sb.ToString().TrimEnd();
            }

            var names = SplitCsv(moduleNames);
            var current = names.First();
            var output = new StringBuilder();
            output.AppendLine($"# Task Context: {string.Join(", ", names)}");
            output.AppendLine();

            foreach (var name in names)
            {
                var target = Uri.EscapeDataString(name);
                var currentValue = Uri.EscapeDataString(current);
                var active = Uri.EscapeDataString(string.Join(",", names));
                var result = await api.GetAsync($"/api/graph/context?target={target}&current={currentValue}&activeModules={active}");
                var context = result.TryGetProperty("context", out var contextObject) ? contextObject : default;
                var session = result.TryGetProperty("session", out var sessionObject) ? sessionObject : default;

                output.AppendLine($"## {name}");
                output.AppendLine($"- Summary: {GetString(context, "summary") ?? "(none)"}");
                output.AppendLine($"- Boundary: {GetString(context, "boundary") ?? "(none)"}");
                AppendIfPresent(output, "Identity", GetString(context, "identityContent"));
                AppendIfPresent(output, "Lessons", GetString(context, "lessonsContent"));
                AppendIfPresent(output, "Active", GetString(context, "activeContent"));

                if (context.ValueKind == JsonValueKind.Object &&
                    context.TryGetProperty("constraints", out var constraints) &&
                    constraints.ValueKind == JsonValueKind.Array)
                {
                    output.AppendLine("- Constraints:");
                    foreach (var constraint in constraints.EnumerateArray())
                        output.AppendLine($"  - {constraint.GetString()}");
                }

                if (session.ValueKind == JsonValueKind.Object &&
                    session.TryGetProperty("items", out var items) &&
                    items.ValueKind == JsonValueKind.Array &&
                    items.GetArrayLength() > 0)
                {
                    output.AppendLine("- Session:");
                    foreach (var item in items.EnumerateArray())
                        output.AppendLine($"  - [{GetString(item, "id")}] {GetString(item, "summary") ?? GetString(item, "content") ?? "(no summary)"}");
                }

                output.AppendLine();
            }

            return output.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool, Description("运行架构治理检查。")]
    public async Task<string> validate_architecture()
    {
        try
        {
            var result = await api.GetAsync("/api/governance/validate");
            var healthy = result.TryGetProperty("healthy", out var healthyFlag) && healthyFlag.ValueKind == JsonValueKind.True;
            return healthy
                ? "# Architecture Report\n\nArchitecture is healthy."
                : JsonSerializer.Serialize(result, PrettyJson);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool, Description("分析 session 和 memory 的升级建议。")]
    public async Task<string> evolve_knowledge(
        [Description("模块名或节点 ID，留空表示全量分析。")] string? nodeIdOrName = null,
        [Description("最多返回建议数，默认 50。")] int maxSuggestions = 50)
    {
        try
        {
            var result = await api.PostAsync("/api/governance/evolve", new
            {
                nodeIdOrName,
                maxSuggestions = Math.Max(maxSuggestions, 1)
            });
            return FormatEvolutionResult(result);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool, Description("按模块压缩短期记忆并提炼为长期知识。")]
    public async Task<string> condense_module_knowledge(
        [Description("模块名或节点 ID。")] string nodeIdOrName,
        [Description("最多参与提炼的源记忆数，默认 200。")] int maxSourceMemories = 200)
    {
        try
        {
            var result = await api.PostAsync("/api/governance/condense/node", new
            {
                nodeIdOrName,
                maxSourceMemories = Math.Max(maxSourceMemories, 1)
            });
            return FormatCondenseResult(result, isBatch: false);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool, Description("批量压缩全部模块知识。")]
    public async Task<string> condense_all_module_knowledge(
        [Description("每个模块最多参与提炼的源记忆数，默认 200。")] int maxSourceMemories = 200)
    {
        try
        {
            var result = await api.PostAsync("/api/governance/condense/all", new
            {
                maxSourceMemories = Math.Max(maxSourceMemories, 1)
            });
            return FormatCondenseResult(result, isBatch: true);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool, Description("查看 CrossWork 列表。")]
    public async Task<string> list_crossworks(
        [Description("按模块名过滤，留空返回全部。")] string? moduleName = null)
    {
        try
        {
            var result = await api.GetAsync("/api/modules/crossworks");
            var source = result.ValueKind == JsonValueKind.Array ? result.EnumerateArray().ToList() : [];
            if (!string.IsNullOrWhiteSpace(moduleName))
            {
                source = source.Where(crossWork =>
                    crossWork.TryGetProperty("participants", out var participants) &&
                    participants.ValueKind == JsonValueKind.Array &&
                    participants.EnumerateArray().Any(participant =>
                        string.Equals(GetString(participant, "moduleName"), moduleName, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
            }

            return source.Count == 0
                ? "No matching CrossWork found."
                : JsonSerializer.Serialize(source, PrettyJson);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool, Description("查看完整架构清单。")]
    public async Task<string> get_manifest()
    {
        try
        {
            var result = await api.GetAsync("/api/modules/manifest");
            return JsonSerializer.Serialize(result, PrettyJson);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool, Description("查看全部部门。")]
    public async Task<string> list_disciplines()
    {
        try
        {
            var result = await api.GetAsync("/api/modules/disciplines");
            return JsonSerializer.Serialize(result, PrettyJson);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool, Description("创建或更新一个部门。")]
    public async Task<string> register_discipline(
        [Description("部门 ID。")] string id,
        [Description("部门显示名。")] string displayName,
        [Description("角色 ID。")] string roleId = "coder")
    {
        try
        {
            var payload = new
            {
                id,
                displayName,
                roleId,
                layers = new[] { new { level = 0, name = "L0" } }
            };
            var result = await api.PostAsync("/api/modules/disciplines", payload);
            return JsonSerializer.Serialize(result, PrettyJson);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool, Description("创建或更新一个模块。")]
    public async Task<string> register_module(
        [Description("模块名。")] string name,
        [Description("所属部门 ID。")] string discipline,
        [Description("模块路径。")] string path,
        [Description("层级。")] int layer = 0,
        [Description("父模块 ID 或名称。")] string? parentModuleId = null,
        [Description("受管路径，逗号分隔。")] string? managedPaths = null,
        [Description("依赖，逗号分隔。")] string? dependencies = null,
        [Description("摘要。")] string? summary = null,
        [Description("边界。")] string? boundary = null,
        [Description("Public API，逗号分隔。")] string? publicApi = null,
        [Description("约束，逗号分隔。")] string? constraints = null,
        [Description("元数据 JSON。")] string? metadata = null,
        [Description("维护者。")] string? maintainer = null)
    {
        try
        {
            Dictionary<string, string>? metadataObject = null;
            if (!string.IsNullOrWhiteSpace(metadata))
                metadataObject = JsonSerializer.Deserialize<Dictionary<string, string>>(metadata);

            var payload = new
            {
                discipline,
                module = new TopologyModuleDefinition
                {
                    Id = name.Trim(),
                    Name = name,
                    Path = path,
                    Layer = Math.Max(layer, 0),
                    ParentModuleId = parentModuleId,
                    ManagedPaths = SplitCsvOrNull(managedPaths),
                    Dependencies = SplitCsv(dependencies),
                    Summary = summary,
                    Boundary = boundary,
                    PublicApi = SplitCsvOrNull(publicApi),
                    Constraints = SplitCsvOrNull(constraints),
                    Metadata = metadataObject,
                    Maintainer = maintainer
                }
            };

            var result = await api.PostAsync("/api/modules/", payload);
            return JsonSerializer.Serialize(result, PrettyJson);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool, Description("删除模块。")]
    public async Task<string> delete_module([Description("模块名。")] string name)
    {
        try
        {
            var target = Uri.EscapeDataString(name);
            var result = await api.DeleteAsync($"/api/modules/{target}");
            return JsonSerializer.Serialize(result, PrettyJson);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool, Description("创建或更新一个 CrossWork。")]
    public async Task<string> register_crosswork(
        [Description("CrossWork 名称。")] string name,
        [Description("参与者 JSON 数组。")] string participants,
        [Description("描述。")] string? description = null,
        [Description("业务域。")] string? feature = null)
    {
        try
        {
            var parts = JsonSerializer.Deserialize<List<TopologyCrossWorkParticipantDefinition>>(participants);
            if (parts is not { Count: > 0 })
                return "Error: participants must contain at least one item.";

            var payload = new
            {
                name,
                description,
                feature,
                participants = parts
            };
            var result = await api.PostAsync("/api/modules/crossworks", payload);
            return JsonSerializer.Serialize(result, PrettyJson);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool, Description("删除 CrossWork。")]
    public async Task<string> delete_crosswork([Description("CrossWork ID。")] string id)
    {
        try
        {
            var target = Uri.EscapeDataString(id);
            var result = await api.DeleteAsync($"/api/modules/crossworks/{target}");
            return JsonSerializer.Serialize(result, PrettyJson);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool, Description("Create or update a module with an explicit stable id.")]
    public async Task<string> upsert_module(
        [Description("Stable module id.")] string id,
        [Description("Module name.")] string name,
        [Description("Discipline id.")] string discipline,
        [Description("Module path.")] string path,
        [Description("Layer number.")] int layer = 0,
        [Description("Parent module id or name.")] string? parentModuleId = null,
        [Description("Managed paths, comma separated.")] string? managedPaths = null,
        [Description("Dependencies, comma separated.")] string? dependencies = null,
        [Description("Summary.")] string? summary = null,
        [Description("Boundary.")] string? boundary = null,
        [Description("Public API, comma separated.")] string? publicApi = null,
        [Description("Constraints, comma separated.")] string? constraints = null,
        [Description("Metadata JSON.")] string? metadata = null,
        [Description("Maintainer.")] string? maintainer = null)
    {
        try
        {
            Dictionary<string, string>? metadataObject = null;
            if (!string.IsNullOrWhiteSpace(metadata))
                metadataObject = JsonSerializer.Deserialize<Dictionary<string, string>>(metadata);

            var payload = new
            {
                discipline,
                module = new TopologyModuleDefinition
                {
                    Id = id.Trim(),
                    Name = name,
                    Path = path,
                    Layer = Math.Max(layer, 0),
                    ParentModuleId = parentModuleId,
                    ManagedPaths = SplitCsvOrNull(managedPaths),
                    Dependencies = SplitCsv(dependencies),
                    Summary = summary,
                    Boundary = boundary,
                    PublicApi = SplitCsvOrNull(publicApi),
                    Constraints = SplitCsvOrNull(constraints),
                    Metadata = metadataObject,
                    Maintainer = maintainer
                }
            };

            var result = await api.PostAsync("/api/modules/", payload);
            return JsonSerializer.Serialize(result, PrettyJson);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    private static string FormatEvolutionResult(JsonElement result)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Knowledge Evolution");
        sb.AppendLine();
        sb.AppendLine($"- Session -> Memory: {GetInt(result, "sessionToMemoryCount")}");
        sb.AppendLine($"- Memory -> Knowledge: {GetInt(result, "memoryToKnowledgeCount")}");

        var filterNodeName = GetString(result, "filterNodeName");
        if (!string.IsNullOrWhiteSpace(filterNodeName))
            sb.AppendLine($"- Filter: {filterNodeName}");

        if (!result.TryGetProperty("suggestions", out var suggestions) ||
            suggestions.ValueKind != JsonValueKind.Array ||
            suggestions.GetArrayLength() == 0)
        {
            sb.AppendLine();
            sb.AppendLine("No evolution suggestions.");
            return sb.ToString().TrimEnd();
        }

        sb.AppendLine();
        sb.AppendLine("## Suggestions");
        sb.AppendLine();
        foreach (var item in suggestions.EnumerateArray())
        {
            var currentLayer = GetString(item, "currentLayer") ?? "unknown";
            var targetLayer = GetString(item, "targetLayer") ?? "unknown";
            var nodeName = GetString(item, "nodeName") ?? GetString(item, "nodeId") ?? "(unscoped)";
            var summary = GetString(item, "summary") ?? "(no summary)";
            var reason = GetString(item, "reason") ?? "(no reason)";
            var confidence = GetString(item, "confidence") ?? "0";

            sb.AppendLine($"- [{currentLayer} -> {targetLayer}] {nodeName}: {summary}");
            sb.AppendLine($"  reason: {reason}");
            sb.AppendLine($"  confidence: {confidence}");

            if (item.TryGetProperty("candidateModuleNames", out var names) &&
                names.ValueKind == JsonValueKind.Array &&
                names.GetArrayLength() > 0)
            {
                var modules = string.Join(", ", names.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)));
                sb.AppendLine($"  modules: {modules}");
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static string FormatCondenseResult(JsonElement result, bool isBatch)
    {
        if (!isBatch)
            return FormatCondenseNode(result);

        var sb = new StringBuilder();
        sb.AppendLine("# Knowledge Condense");
        sb.AppendLine();
        sb.AppendLine($"- Total Nodes: {GetInt(result, "total")}");
        sb.AppendLine($"- Condensed Nodes: {GetInt(result, "condensed")}");
        sb.AppendLine($"- Archived Memories: {GetInt(result, "archived")}");

        if (result.TryGetProperty("results", out var results) &&
            results.ValueKind == JsonValueKind.Array &&
            results.GetArrayLength() > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Nodes");
            sb.AppendLine();
            foreach (var item in results.EnumerateArray())
                sb.AppendLine($"- {FormatCondenseNodeLine(item)}");
        }

        return sb.ToString().TrimEnd();
    }

    private static string FormatCondenseNode(JsonElement result)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Knowledge Condense");
        sb.AppendLine();
        sb.AppendLine($"- Node: {GetString(result, "nodeName") ?? GetString(result, "nodeId") ?? "(unknown)"}");
        sb.AppendLine($"- Sources: {GetInt(result, "sourceCount")} (session {GetInt(result, "sessionSourceCount")}, memory {GetInt(result, "memorySourceCount")})");
        sb.AppendLine($"- Archived: {GetInt(result, "archivedCount")}");
        AppendIfPresent(sb, "Identity Memory", GetString(result, "newIdentityMemoryId"));
        AppendIfPresent(sb, "Upgrade Trail", GetString(result, "upgradeTrailMemoryId"));
        AppendIfPresent(sb, "Summary", GetString(result, "summary"));
        return sb.ToString().TrimEnd();
    }

    private static string FormatCondenseNodeLine(JsonElement item)
    {
        var nodeName = GetString(item, "nodeName") ?? GetString(item, "nodeId") ?? "(unknown)";
        var sourceCount = GetInt(item, "sourceCount");
        var sessionCount = GetInt(item, "sessionSourceCount");
        var memoryCount = GetInt(item, "memorySourceCount");
        var archivedCount = GetInt(item, "archivedCount");
        var identityId = GetString(item, "newIdentityMemoryId");
        var trailId = GetString(item, "upgradeTrailMemoryId");

        var suffix = new List<string>
        {
            $"sources={sourceCount}",
            $"session={sessionCount}",
            $"memory={memoryCount}",
            $"archived={archivedCount}"
        };

        if (!string.IsNullOrWhiteSpace(identityId))
            suffix.Add($"identity={identityId}");
        if (!string.IsNullOrWhiteSpace(trailId))
            suffix.Add($"trail={trailId}");

        return $"{nodeName} ({string.Join(", ", suffix)})";
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        if (!element.TryGetProperty(propertyName, out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static int GetInt(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return 0;

        if (!element.TryGetProperty(propertyName, out var value))
            return 0;

        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : 0;
    }

    private static void AppendIfPresent(StringBuilder sb, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            sb.AppendLine($"- {label}: {value}");
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
}
