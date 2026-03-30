using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Dna.Client.Services;
using Dna.Knowledge.Models;
using ModelContextProtocol.Server;

namespace Dna.Client.Interfaces.Mcp;

[McpServerToolType]
public sealed class KnowledgeTools(DnaServerApi api)
{
    private static readonly JsonSerializerOptions PrettyJson = new() { WriteIndented = true };

    [McpServerTool, Description("查看项目拓扑全貌。")]
    public async Task<string> get_topology()
    {
        try
        {
            var topo = await api.GetAsync("/api/topology");
            var summary = GetString(topo, "summary") ?? "拓扑读取成功";
            var modules = topo.TryGetProperty("modules", out var mArr) && mArr.ValueKind == JsonValueKind.Array
                ? mArr.GetArrayLength()
                : 0;
            var edges = topo.TryGetProperty("relationEdges", out var rArr) && rArr.ValueKind == JsonValueKind.Array
                ? rArr.GetArrayLength()
                : 0;
            return $"# 知识图谱拓扑\n\n- {summary}\n- 模块数: {modules}\n- 关系边: {edges}";
        }
        catch (Exception ex)
        {
            return $"错误：{ex.Message}";
        }
    }

    [McpServerTool, Description("对一组模块按依赖关系排序。")]
    public async Task<string> get_dependency_order(
        [Description("模块名列表，逗号分隔。示例: 'Combat,Character,UI'")] string moduleNames)
    {
        try
        {
            var value = Uri.EscapeDataString(moduleNames ?? string.Empty);
            var result = await api.GetAsync($"/api/plan?modules={value}");
            var order = result.TryGetProperty("orderedModules", out var arr) && arr.ValueKind == JsonValueKind.Array
                ? string.Join(" -> ", arr.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)))
                : "(无)";
            var hasCycle = result.TryGetProperty("hasCycle", out var c) && c.ValueKind == JsonValueKind.True;
            var cycle = GetString(result, "cycleDescription");
            var sb = new StringBuilder();
            sb.AppendLine("## 依赖排序");
            sb.AppendLine();
            sb.AppendLine($"- 顺序: {order}");
            if (hasCycle) sb.AppendLine($"- 循环依赖: {cycle ?? "已检测到"}");
            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"错误：{ex.Message}";
        }
    }

    [McpServerTool, Description("按关键词模糊搜索模块。")]
    public async Task<string> search_modules(
        [Description("搜索关键词")] string query,
        [Description("最多返回条数（默认 8，上限 20）")] int maxResults = 8)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
                return "错误：query 至少 2 个字符。";

            var q = Uri.EscapeDataString(query.Trim());
            var result = await api.GetAsync($"/api/graph/search?q={q}&maxResults={Math.Clamp(maxResults, 1, 20)}");
            if (!result.TryGetProperty("results", out var arr) || arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() == 0)
                return $"未找到与 '{query}' 相关的模块。";

            var sb = new StringBuilder();
            sb.AppendLine($"## 模块检索结果（{arr.GetArrayLength()}）");
            sb.AppendLine();
            foreach (var item in arr.EnumerateArray())
            {
                sb.AppendLine($"- **{GetString(item, "name") ?? "(unknown)"}** ({GetString(item, "discipline") ?? "generic"})");
                var summary = GetString(item, "summary");
                if (!string.IsNullOrWhiteSpace(summary)) sb.AppendLine($"  {summary}");
            }
            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"错误：{ex.Message}";
        }
    }

    [McpServerTool, Description("获取模块上下文。")]
    public async Task<string> get_context(
        [Description("模块名，支持逗号分隔多个。留空返回模块速查。")] string? moduleNames = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(moduleNames))
            {
                var result = await api.PostAsync("/api/graph/begin-task", new { });
                if (!result.TryGetProperty("modules", out var modules) || modules.ValueKind != JsonValueKind.Array)
                    return "未获取到模块列表。";

                var sb = new StringBuilder();
                sb.AppendLine("# 项目模块速查");
                sb.AppendLine();
                foreach (var module in modules.EnumerateArray())
                    sb.AppendLine($"- {GetString(module, "name")} ({GetString(module, "discipline")})");
                sb.AppendLine();
                sb.AppendLine("提示：调用 get_context(\"模块名\") 获取详细上下文。");
                return sb.ToString();
            }

            var names = SplitCsv(moduleNames);
            var current = names.First();
            var sb2 = new StringBuilder();
            sb2.AppendLine($"# 开始任务: {string.Join(", ", names)}");
            sb2.AppendLine();

            foreach (var name in names)
            {
                var target = Uri.EscapeDataString(name);
                var cur = Uri.EscapeDataString(current);
                var active = Uri.EscapeDataString(string.Join(",", names));
                var result = await api.GetAsync($"/api/graph/context?target={target}&current={cur}&activeModules={active}");
                var context = result.TryGetProperty("context", out var ctx) ? ctx : default;
                sb2.AppendLine($"## {name}");
                sb2.AppendLine($"- Summary: {GetString(context, "summary") ?? "(无)"}");
                sb2.AppendLine($"- Boundary: {GetString(context, "boundary") ?? "(无)"}");
                if (context.ValueKind == JsonValueKind.Object && context.TryGetProperty("constraints", out var cArr) && cArr.ValueKind == JsonValueKind.Array)
                {
                    sb2.AppendLine("- Constraints:");
                    foreach (var c in cArr.EnumerateArray())
                        sb2.AppendLine($"  - {c.GetString()}");
                }
                sb2.AppendLine();
            }

            return sb2.ToString();
        }
        catch (Exception ex)
        {
            return $"错误：{ex.Message}";
        }
    }

    [McpServerTool, Description("运行架构健康检查。")]
    public async Task<string> validate_architecture()
    {
        try
        {
            var result = await api.GetAsync("/api/governance/validate");
            var healthy = result.TryGetProperty("healthy", out var h) && h.ValueKind == JsonValueKind.True;
            if (healthy) return "# 架构治理报告\n\n架构健康，未发现问题。";
            return JsonSerializer.Serialize(result, PrettyJson);
        }
        catch (Exception ex)
        {
            return $"错误：{ex.Message}";
        }
    }

    [McpServerTool, Description("按模块压缩短期记忆并提炼为长期知识。")]
    public async Task<string> condense_module_knowledge(
        [Description("模块名或节点 ID")] string nodeIdOrName,
        [Description("最多参与提炼的源记忆数（默认 200）")] int maxSourceMemories = 200)
    {
        try
        {
            var result = await api.PostAsync("/api/governance/condense/node", new
            {
                nodeIdOrName,
                maxSourceMemories = Math.Max(maxSourceMemories, 1)
            });
            return JsonSerializer.Serialize(result, PrettyJson);
        }
        catch (Exception ex)
        {
            return $"错误：{ex.Message}";
        }
    }

    [McpServerTool, Description("批量压缩全部模块知识。")]
    public async Task<string> condense_all_module_knowledge(
        [Description("每个模块最多参与提炼的源记忆数（默认 200）")] int maxSourceMemories = 200)
    {
        try
        {
            var result = await api.PostAsync("/api/governance/condense/all", new
            {
                maxSourceMemories = Math.Max(maxSourceMemories, 1)
            });
            return JsonSerializer.Serialize(result, PrettyJson);
        }
        catch (Exception ex)
        {
            return $"错误：{ex.Message}";
        }
    }

    [McpServerTool, Description("查看 CrossWork 声明。")]
    public async Task<string> list_crossworks(
        [Description("按模块名过滤，留空返回全部")] string? moduleName = null)
    {
        try
        {
            var result = await api.GetAsync("/api/modules/crossworks");
            var source = result.ValueKind == JsonValueKind.Array ? result.EnumerateArray().ToList() : [];
            if (!string.IsNullOrWhiteSpace(moduleName))
            {
                source = source.Where(cw =>
                    cw.TryGetProperty("participants", out var ps) &&
                    ps.ValueKind == JsonValueKind.Array &&
                    ps.EnumerateArray().Any(p =>
                        string.Equals(GetString(p, "moduleName"), moduleName, StringComparison.OrdinalIgnoreCase))).ToList();
            }
            return source.Count == 0
                ? "当前没有匹配的 CrossWork。"
                : JsonSerializer.Serialize(source, PrettyJson);
        }
        catch (Exception ex)
        {
            return $"错误：{ex.Message}";
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
            return $"错误：{ex.Message}";
        }
    }

    [McpServerTool, Description("查看所有已定义部门。")]
    public async Task<string> list_disciplines()
    {
        try
        {
            var result = await api.GetAsync("/api/modules/disciplines");
            return JsonSerializer.Serialize(result, PrettyJson);
        }
        catch (Exception ex)
        {
            return $"错误：{ex.Message}";
        }
    }

    [McpServerTool, Description("创建或修改一个部门。")]
    public async Task<string> register_discipline(
        [Description("部门 ID")] string id,
        [Description("部门显示名")] string displayName,
        [Description("角色 ID")] string roleId = "coder")
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
            return $"错误：{ex.Message}";
        }
    }

    [McpServerTool, Description("注册或修改一个模块。")]
    public async Task<string> register_module(
        [Description("模块名")] string name,
        [Description("所属部门 ID")] string discipline,
        [Description("模块路径")] string path,
        [Description("依赖，逗号分隔")] string? dependencies = null,
        [Description("职责摘要")] string? summary = null,
        [Description("边界")] string? boundary = null,
        [Description("对外接口，逗号分隔")] string? publicApi = null,
        [Description("约束规则，逗号分隔")] string? constraints = null,
        [Description("扩展属性 JSON")] string? metadata = null,
        [Description("维护者")] string? maintainer = null)
    {
        try
        {
            Dictionary<string, string>? metadataObj = null;
            if (!string.IsNullOrWhiteSpace(metadata))
                metadataObj = JsonSerializer.Deserialize<Dictionary<string, string>>(metadata);

            var payload = new
            {
                discipline,
                module = new ModuleRegistration
                {
                    Name = name,
                    Path = path,
                    Layer = 0,
                    Dependencies = SplitCsv(dependencies),
                    Summary = summary,
                    Boundary = boundary,
                    PublicApi = SplitCsvOrNull(publicApi),
                    Constraints = SplitCsvOrNull(constraints),
                    Metadata = metadataObj,
                    Maintainer = maintainer
                }
            };

            var result = await api.PostAsync("/api/modules/", payload);
            return JsonSerializer.Serialize(result, PrettyJson);
        }
        catch (Exception ex)
        {
            return $"错误：{ex.Message}";
        }
    }

    [McpServerTool, Description("删除模块。")]
    public async Task<string> delete_module([Description("模块名")] string name)
    {
        try
        {
            var target = Uri.EscapeDataString(name);
            var result = await api.DeleteAsync($"/api/modules/{target}");
            return JsonSerializer.Serialize(result, PrettyJson);
        }
        catch (Exception ex)
        {
            return $"错误：{ex.Message}";
        }
    }

    [McpServerTool, Description("创建或修改一个 CrossWork。")]
    public async Task<string> register_crosswork(
        [Description("CrossWork 名称")] string name,
        [Description("参与者 JSON 数组")] string participants,
        [Description("描述")] string? description = null,
        [Description("关联业务系统")] string? feature = null)
    {
        try
        {
            var parts = JsonSerializer.Deserialize<List<CrossWorkParticipantRegistration>>(participants);
            if (parts is not { Count: > 0 }) return "错误：participants 至少包含一个参与者。";

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
            return $"错误：{ex.Message}";
        }
    }

    [McpServerTool, Description("删除 CrossWork。")]
    public async Task<string> delete_crosswork([Description("CrossWork 的 ID")] string id)
    {
        try
        {
            var target = Uri.EscapeDataString(id);
            var result = await api.DeleteAsync($"/api/modules/crossworks/{target}");
            return JsonSerializer.Serialize(result, PrettyJson);
        }
        catch (Exception ex)
        {
            return $"错误：{ex.Message}";
        }
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
