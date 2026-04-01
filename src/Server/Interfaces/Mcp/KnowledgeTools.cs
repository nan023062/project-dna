using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dna.Core.Logging;
using Dna.Knowledge;
using Dna.Knowledge.Models;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Dna.Interfaces.Mcp;

/// <summary>
/// 知识图谱 MCP 工具集：拓扑、检索、执行计划、上下文、治理、CrossWork。
/// </summary>
[McpServerToolType]
public class KnowledgeTools(
    IGraphEngine graph,
    IGovernanceEngine governance,
    ILogger<KnowledgeTools> logger)
{
    // ═══════════════════════════════════════════
    //  拓扑 & 查询
    // ═══════════════════════════════════════════

    [McpServerTool, Description(
        "查看项目的模块拓扑全貌：所有模块按部门分组展示，包含依赖关系和 CrossWork 统计。" +
        "适用于：首次了解项目整体结构、查看模块间依赖关系。" +
        "返回内容包含部门→模块的树状结构。" +
        "如果只需要查看单个模块的详细信息，请用 get_context。")]
    public string get_topology()
    {
        logger.LogInformation(LogEvents.Mcp, "get_topology()");
        var topo = graph.BuildTopology();

        var sb = new StringBuilder();
        sb.AppendLine("# 知识图谱拓扑");
        sb.AppendLine();
        sb.AppendLine($"- 模块数: {topo.Nodes.Count}");
        sb.AppendLine($"- 依赖边: {topo.Edges.Count}");
        sb.AppendLine($"- CrossWork: {topo.CrossWorks.Count}");
        sb.AppendLine($"- 构建时间: {topo.BuiltAt:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine();

        foreach (var group in topo.Nodes.GroupBy(n => n.Discipline ?? "generic", StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key))
        {
            sb.AppendLine($"## Department: {group.Key}");
            foreach (var n in group.OrderBy(m => m.Name))
            {
                sb.AppendLine($"- {n.Name} ({n.Discipline ?? "generic"})");
                AppendDeps(sb, n);
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    [McpServerTool, Description(
        "对一组模块按依赖关系排序：被依赖的模块排在前面。" +
        "适用于需要按正确顺序修改多个模块时。" +
        "如果检测到循环依赖会在结果中标注。")]
    public string get_dependency_order(
        [Description("模块名列表，逗号分隔。示例: 'Combat,Character,UI'")] string moduleNames)
    {
        logger.LogInformation(LogEvents.Mcp, "get_dependency_order() modules={Modules}", moduleNames);
        graph.BuildTopology();

        var names = SplitCsv(moduleNames);
        if (names.Count == 0) return "错误：请至少提供一个模块名。";

        var plan = graph.GetExecutionPlan(names);
        var sb = new StringBuilder();
        sb.AppendLine("## 依赖排序");
        sb.AppendLine();
        if (plan.HasCycle)
            sb.AppendLine($"- 检测到循环依赖: {plan.CycleDescription}");
        sb.AppendLine();

        for (var i = 0; i < plan.OrderedModules.Count; i++)
            sb.AppendLine($"{i + 1}. {plan.OrderedModules[i]}");

        return sb.ToString();
    }

    [McpServerTool, Description(
        "按关键词模糊搜索模块。匹配模块名、路径、摘要、关键词和依赖名。" +
        "适用于：不确定模块的准确名称时，通过关键词定位。" +
        "与 recall 的区别：search_modules 搜索模块注册信息，recall 搜索记忆内容。")]
    public string search_modules(
        [Description("搜索关键词，至少 2 个字符。示例: 'combat'、'UI'、'network'")] string query,
        [Description("最多返回条数（默认 8，上限 20）")] int maxResults = 8)
    {
        logger.LogInformation(LogEvents.Mcp, "search_modules() query={Query}", query);
        graph.BuildTopology();

        var q = query.Trim().ToLowerInvariant();
        if (q.Length < 2) return "错误：query 至少 2 个字符。";

        var result = graph.GetAllModules()
            .Select(m => new { Module = m, Score = ScoreModule(m, q) })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Module.Name)
            .Take(Math.Clamp(maxResults, 1, 20))
            .ToList();

        if (result.Count == 0)
            return $"未找到与 '{query}' 相关的模块。";

        var sb = new StringBuilder();
        sb.AppendLine($"## 模块检索结果（{result.Count}）");
        sb.AppendLine();
        foreach (var r in result)
        {
            var m = r.Module;
            sb.AppendLine($"- **{m.Name}** ({m.Discipline ?? "generic"})");
            if (!string.IsNullOrWhiteSpace(m.Summary))
                sb.AppendLine($"  {m.Summary}");
        }
        return sb.ToString();
    }

    // ═══════════════════════════════════════════
    //  上下文 & 任务
    // ═══════════════════════════════════════════

    [McpServerTool, Description(
        "获取模块上下文：不指定模块时返回所有模块速查列表，指定模块时返回详细上下文（依赖、约束、CrossWork、历史教训）。支持逗号分隔多个模块。")]
    public string get_context(
        [Description("要开始工作的模块名，支持逗号分隔多个。留空返回模块速查列表。示例: 'Combat' 或 'Combat,Character'")] string? moduleNames = null)
    {
        var topo = graph.BuildTopology();

        if (string.IsNullOrWhiteSpace(moduleNames))
        {
            var sb = new StringBuilder();
            sb.AppendLine("# 项目模块速查");
            sb.AppendLine();
            sb.AppendLine($"- 模块数: {topo.Nodes.Count} | 依赖边: {topo.Edges.Count} | CrossWork: {topo.CrossWorks.Count}");
            sb.AppendLine();
            foreach (var n in topo.Nodes.OrderBy(n => n.Discipline ?? "generic").ThenBy(n => n.Name))
                sb.AppendLine($"- {n.Name} ({n.Discipline ?? "generic"})");
            sb.AppendLine();
            sb.AppendLine("提示：调用 get_context(\"模块名\") 获取详细上下文。");
            return sb.ToString();
        }

        var names = SplitCsv(moduleNames);
        var current = names[0];
        var result = new StringBuilder();
        result.AppendLine($"# 开始任务: {string.Join(", ", names)}");
        result.AppendLine();

        foreach (var name in names)
        {
            var ctx = graph.GetModuleContext(name, current, names);
            result.AppendLine(FormatModuleContext(ctx));
            result.AppendLine("\n---\n");
        }

        var crossWorks = names.SelectMany(n => graph.GetCrossWorksForModule(n)).Distinct().ToList();
        if (crossWorks.Count > 0)
        {
            result.AppendLine("## 相关 CrossWork（业务交叉工作）");
            result.AppendLine();
            foreach (var cw in crossWorks)
            {
                result.AppendLine($"### {cw.Name}");
                if (!string.IsNullOrWhiteSpace(cw.Description))
                    result.AppendLine(cw.Description);
                foreach (var p in cw.Participants)
                {
                    result.AppendLine($"- **{p.ModuleName}**: {p.Role}");
                    if (!string.IsNullOrWhiteSpace(p.Contract))
                        result.AppendLine($"  Contract: {p.Contract}");
                    if (!string.IsNullOrWhiteSpace(p.Deliverable))
                        result.AppendLine($"  交付物: {p.Deliverable}");
                }
                result.AppendLine();
            }
        }

        return result.ToString();
    }

    // ═══════════════════════════════════════════
    //  治理
    // ═══════════════════════════════════════════

    [McpServerTool, Description(
        "运行架构健康检查，检测项目中的结构性问题：循环依赖（建议重组）、" +
        "孤儿节点（无连接的模块）、CrossWork 声明问题、依赖偏差、关键节点预警。" +
        "适用于：重构前评估架构健康度、定期架构巡检、提交前检查是否引入新问题。" +
        "返回详细的问题清单和修复建议。如果架构健康则返回通过提示。")]
    public string validate_architecture()
    {
        logger.LogInformation(LogEvents.Mcp, "validate_architecture()");
        graph.BuildTopology();

        var report = governance.ValidateArchitecture();
        var sb = new StringBuilder();
        sb.AppendLine("# 架构治理报告");
        sb.AppendLine();

        if (report.IsHealthy)
        {
            sb.AppendLine("架构健康，未发现问题。");
            return sb.ToString();
        }

        sb.AppendLine($"发现 {report.TotalIssues} 个问题。");
        sb.AppendLine();

        if (report.CycleSuggestions.Count > 0)
        {
            sb.AppendLine($"## 循环依赖建议（{report.CycleSuggestions.Count} 组）");
            sb.AppendLine();
            foreach (var cycle in report.CycleSuggestions)
            {
                sb.AppendLine($"- {cycle.Message}");
                sb.AppendLine($"  建议: {cycle.Suggestion}");
            }
            sb.AppendLine();
        }

        if (report.OrphanNodes.Count > 0)
        {
            sb.AppendLine($"## 孤儿节点（{report.OrphanNodes.Count} 个）");
            sb.AppendLine();
            foreach (var n in report.OrphanNodes)
                sb.AppendLine($"- {n.Name} ({n.Discipline ?? "generic"}) — 无依赖连接也不在任何 CrossWork 中");
            sb.AppendLine();
        }

        if (report.CrossWorkIssues.Count > 0)
        {
            sb.AppendLine($"## CrossWork 问题（{report.CrossWorkIssues.Count} 条）");
            sb.AppendLine();
            foreach (var issue in report.CrossWorkIssues)
                sb.AppendLine($"- [{issue.CrossWorkName}] {issue.Message}");
            sb.AppendLine();
        }

        if (report.DependencyDrifts.Count > 0)
        {
            sb.AppendLine($"## 依赖偏差（{report.DependencyDrifts.Count} 个模块）");
            sb.AppendLine();
            foreach (var drift in report.DependencyDrifts)
            {
                sb.AppendLine($"- **{drift.ModuleName}**: {drift.Message}");
                if (drift.Suggestion != null) sb.AppendLine($"  建议: {drift.Suggestion}");
            }
            sb.AppendLine();
        }

        if (report.KeyNodeWarnings.Count > 0)
        {
            sb.AppendLine($"## 关键节点预警（{report.KeyNodeWarnings.Count} 个）");
            sb.AppendLine();
            foreach (var warn in report.KeyNodeWarnings)
                sb.AppendLine($"- **{warn.NodeName}**: {warn.Message}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    [McpServerTool, Description(
        "按模块压缩短期记忆并提炼为模块长期知识。" +
        "输入模块名或节点 ID，系统会聚合该模块近期记忆，生成新的 #identity 记忆摘要，" +
        "并将已提炼的 Episodic/Working 记忆归档，降低噪声和检索成本。")]
    public async Task<string> condense_module_knowledge(
        [Description("模块名或节点 ID")] string nodeIdOrName,
        [Description("最多参与提炼的源记忆数（默认 200）")] int maxSourceMemories = 200)
    {
        logger.LogInformation(LogEvents.Mcp, "condense_module_knowledge() node={Node}", nodeIdOrName);
        graph.BuildTopology();

        var result = await governance.CondenseNodeKnowledgeAsync(nodeIdOrName, maxSourceMemories);
        return $"✓ 模块知识压缩完成\n" +
               $"- 节点: {result.NodeName ?? result.NodeId}\n" +
               $"- 源记忆: {result.SourceCount}\n" +
               $"- 归档: {result.ArchivedCount}\n" +
               $"- 新摘要ID: {result.NewIdentityMemoryId ?? "无"}\n" +
               $"- 摘要: {result.Summary ?? "无"}";
    }

    [McpServerTool, Description(
        "批量压缩全部模块知识。会遍历所有 Module/CrossWork 节点，执行记忆提炼和归档。" +
        "适用于定期知识维护任务。")]
    public async Task<string> condense_all_module_knowledge(
        [Description("每个模块最多参与提炼的源记忆数（默认 200）")] int maxSourceMemories = 200)
    {
        logger.LogInformation(LogEvents.Mcp, "condense_all_module_knowledge()");
        graph.BuildTopology();

        var results = await governance.CondenseAllNodesAsync(maxSourceMemories);
        var condensed = results.Count(r => !string.IsNullOrWhiteSpace(r.NewIdentityMemoryId));
        var archived = results.Sum(r => r.ArchivedCount);

        var sb = new StringBuilder();
        sb.AppendLine("✓ 全量模块知识压缩完成");
        sb.AppendLine($"- 节点总数: {results.Count}");
        sb.AppendLine($"- 产生摘要: {condensed}");
        sb.AppendLine($"- 归档记忆: {archived}");
        sb.AppendLine();
        foreach (var r in results.Where(x => !string.IsNullOrWhiteSpace(x.NewIdentityMemoryId)).Take(20))
            sb.AppendLine($"- {r.NodeName ?? r.NodeId}: source={r.SourceCount}, archived={r.ArchivedCount}, identity={r.NewIdentityMemoryId}");
        if (condensed > 20) sb.AppendLine($"... 其余 {condensed - 20} 个节点已省略");
        return sb.ToString();
    }

    // ═══════════════════════════════════════════
    //  CrossWork
    // ═══════════════════════════════════════════

    [McpServerTool, Description(
        "查看 CrossWork（业务交叉工作）声明。CrossWork 定义了多个模块协作完成某个业务功能时各自的角色、接口契约和交付物。" +
        "与模块依赖的区别：依赖是代码层面的引用关系，CrossWork 是业务层面的协作约定（如「角色换装」需要 Character + Equipment + UI 三个模块协作）。" +
        "适用于：了解某个模块参与了哪些跨模块协作、查看协作接口契约。" +
        "可按模块名过滤，留空返回全部 CrossWork。")]
    public string list_crossworks(
        [Description("按模块名过滤，只返回该模块参与的 CrossWork。留空返回全部")] string? moduleName = null)
    {
        logger.LogInformation(LogEvents.Mcp, "list_crossworks() module={Module}", moduleName ?? "(all)");
        graph.BuildTopology();

        var crossWorks = string.IsNullOrWhiteSpace(moduleName)
            ? graph.GetCrossWorks()
            : graph.GetCrossWorksForModule(moduleName);

        if (crossWorks.Count == 0)
            return string.IsNullOrWhiteSpace(moduleName)
                ? "当前项目没有 CrossWork 声明。请通过 register_crosswork 维护。"
                : $"模块 '{moduleName}' 不参与任何 CrossWork。";

        var sb = new StringBuilder();
        sb.AppendLine($"## CrossWork 列表（{crossWorks.Count}）");
        sb.AppendLine();

        foreach (var cw in crossWorks)
        {
            sb.AppendLine($"### {cw.Name}");
            if (!string.IsNullOrWhiteSpace(cw.Feature))
                sb.AppendLine($"Feature: {cw.Feature}");
            if (!string.IsNullOrWhiteSpace(cw.Description))
                sb.AppendLine(cw.Description);
            sb.AppendLine();
            foreach (var p in cw.Participants)
            {
                sb.AppendLine($"- **{p.ModuleName}**: {p.Role}");
                if (!string.IsNullOrWhiteSpace(p.Contract))
                    sb.AppendLine($"  Contract: {p.Contract}");
                if (!string.IsNullOrWhiteSpace(p.Deliverable))
                    sb.AppendLine($"  交付物: {p.Deliverable}");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    // ═══════════════════════════════════════════
    //  架构管理（部门 / 模块 / CrossWork）
    // ═══════════════════════════════════════════

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    [McpServerTool, Description(
        "查看项目的完整架构清单：所有部门、已注册模块、CrossWork 声明。" +
        "这是了解项目架构骨架的入口。返回 JSON 格式的完整 manifest。" +
        "适用于：首次了解项目有哪些部门和模块、检查架构是否已配置、灌入架构前查看现状。")]
    public string get_manifest()
    {
        logger.LogInformation(LogEvents.Mcp, "get_manifest()");
        var arch = graph.GetArchitecture();
        var manifest = graph.GetModulesManifest();

        var result = new
        {
            disciplines = arch.Disciplines.ToDictionary(
                kv => kv.Key,
                kv => new
                {
                    displayName = kv.Value.DisplayName ?? kv.Key,
                    roleId = kv.Value.RoleId,
                    layers = kv.Value.Layers,
                    modules = manifest.Disciplines.GetValueOrDefault(kv.Key, [])
                }),
            crossWorks = manifest.CrossWorks,
            features = manifest.Features
        };
        return JsonSerializer.Serialize(result, JsonOpts);
    }

    [McpServerTool, Description(
        "查看所有已定义的部门。返回部门 ID、显示名、角色 ID 和模块数量。" +
        "适用于：注册模块前确认目标部门。")]
    public string list_disciplines()
    {
        logger.LogInformation(LogEvents.Mcp, "list_disciplines()");
        var arch = graph.GetArchitecture();
        var manifest = graph.GetModulesManifest();

        var sb = new StringBuilder();
        sb.AppendLine("## 部门列表\n");
        foreach (var (id, def) in arch.Disciplines.OrderBy(kv => kv.Key))
        {
            var count = manifest.Disciplines.GetValueOrDefault(id, []).Count;
            sb.AppendLine($"### {def.DisplayName ?? id} (`{id}`)");
            sb.AppendLine($"- 角色: {def.RoleId} | 模块数: {count}");
            sb.AppendLine();
        }
        if (arch.Disciplines.Count == 0)
            sb.AppendLine("暂无部门定义。请使用 `register_discipline` 创建。");
        return sb.ToString();
    }

    [McpServerTool, Description(
        "创建或修改一个部门（Discipline）。" +
        "部门是知识图谱的顶层组织单元（如 gameplay、ui、network）。" +
        "如果部门已存在则更新，不存在则创建。")]
    public string register_discipline(
        [Description("部门 ID（小写英文）。示例: 'gameplay', 'ui', 'network'")] string id,
        [Description("部门显示名。示例: '游戏逻辑', 'UI框架', '网络层'")] string displayName,
        [Description("角色 ID，决定使用哪个上下文解释器（默认 coder）。可选: coder/designer/artist/ta")] string roleId = "coder")
    {
        logger.LogInformation(LogEvents.Mcp, "register_discipline() id={Id}", id);

        var trimmedId = id.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(trimmedId))
            return "错误：部门 ID 不能为空";

        graph.UpsertDiscipline(trimmedId, displayName, roleId, []);
        return $"✓ 部门 '{trimmedId}' 已保存（{displayName}）";
    }

    [McpServerTool, Description(
        "注册或修改一个模块。模块是知识图谱的核心节点，代表项目中的一个代码单元（Assembly、命名空间、目录等）。" +
        "必填：所属部门(discipline)、路径(path)。" +
        "可选：依赖(dependencies)、职责摘要(summary)、边界模式(boundary)、对外接口(publicApi)、约束规则(constraints)、自定义属性(metadata)。" +
        "如果模块已存在（按 name 匹配）则更新，不存在则创建。注册后系统自动重建拓扑图。" +
        "结构化属性（boundary/publicApi/constraints）是模块的权威描述，get_context 时优先展示。" +
        "metadata 是自定义扩展字段（如 performanceBudget、gcPolicy），项目可自由定义。")]
    public string register_module(
        [Description("模块名（唯一标识）。示例: 'Combat', 'CharacterSystem', 'UIFramework'")] string name,
        [Description("所属部门 ID。必须是已注册的部门。示例: 'gameplay'")] string discipline,
        [Description("模块路径（相对于项目根目录）。示例: 'Assets/Scripts/Combat'")] string path,
        [Description("层级编号。示例: 0 表示部门下第一层模块，1 表示其子模块")] int layer = 0,
        [Description("父模块 ID 或名称。留空表示部门下顶层模块")] string? parentModuleId = null,
        [Description("受管路径列表，逗号分隔。留空时默认只使用 path")] string? managedPaths = null,
        [Description("依赖的其他模块名，逗号分隔。示例: 'EventSystem,CharacterSystem'")] string? dependencies = null,
        [Description("模块职责一句话描述。示例: '负责战斗伤害计算、技能释放和 Buff 管理'")] string? summary = null,
        [Description("边界模式: open（任何人可调用）/ semi-open（通过指定接口调用）/ closed（仅内部使用）")] string? boundary = null,
        [Description("对外接口列表，逗号分隔。示例: 'IDamageCalculator,ICombatEvents'")] string? publicApi = null,
        [Description("约束规则列表，逗号分隔。示例: '禁止外部直接修改伤害公式,所有调用走 EventSystem'")] string? constraints = null,
        [Description("自定义扩展属性 JSON: {\"performanceBudget\":\"2ms\",\"gcPolicy\":\"zero-alloc\"}")] string? metadata = null,
        [Description("维护者（可选）")] string? maintainer = null)
    {
        logger.LogInformation(LogEvents.Mcp, "register_module() name={Name} disc={Disc}", name, discipline);

        if (string.IsNullOrWhiteSpace(name))
            return "错误：模块名不能为空";
        if (string.IsNullOrWhiteSpace(discipline))
            return "错误：部门不能为空";
        if (string.IsNullOrWhiteSpace(path))
            return "错误：路径不能为空";

        var normalizedDiscipline = discipline.Trim().ToLowerInvariant();
        graph.UpsertDiscipline(normalizedDiscipline, normalizedDiscipline, "coder", []);

        Dictionary<string, string>? metaDict = null;
        if (!string.IsNullOrWhiteSpace(metadata))
        {
            try { metaDict = JsonSerializer.Deserialize<Dictionary<string, string>>(metadata, JsonOpts); }
            catch (Exception ex) { return $"错误：metadata 解析失败 — {ex.Message}"; }
        }

        var module = new ModuleRegistration
        {
            Name = name.Trim(),
            Path = path.Trim(),
            Layer = Math.Max(layer, 0),
            ParentModuleId = parentModuleId,
            ManagedPaths = managedPaths != null ? SplitCsv(managedPaths) : null,
            Dependencies = dependencies != null ? SplitCsv(dependencies) : [],
            Maintainer = maintainer,
            Summary = summary,
            Boundary = boundary,
            PublicApi = publicApi != null ? SplitCsv(publicApi) : null,
            Constraints = constraints != null ? SplitCsv(constraints) : null,
            Metadata = metaDict
        };

        try
        {
            graph.RegisterModule(normalizedDiscipline, module);
            graph.BuildTopology();

            var sb = new StringBuilder();
            sb.AppendLine($"✓ 模块 '{name}' 已注册到 {normalizedDiscipline}");
            sb.AppendLine($"路径: {path}");
            if (!string.IsNullOrWhiteSpace(summary)) sb.AppendLine($"职责: {summary}");
            if (!string.IsNullOrWhiteSpace(boundary)) sb.AppendLine($"边界: {boundary}");
            if (module.PublicApi is { Count: > 0 }) sb.AppendLine($"接口: {string.Join(", ", module.PublicApi)}");
            if (module.Constraints is { Count: > 0 }) sb.AppendLine($"约束: {string.Join("; ", module.Constraints)}");
            if (module.Dependencies.Count > 0) sb.AppendLine($"依赖: {string.Join(", ", module.Dependencies)}");
            if (metaDict is { Count: > 0 }) sb.AppendLine($"扩展: {string.Join(", ", metaDict.Select(kv => $"{kv.Key}={kv.Value}"))}");
            return sb.ToString();
        }
        catch (InvalidOperationException ex)
        {
            return $"错误：{ex.Message}";
        }
    }

    [McpServerTool, Description(
        "删除一个已注册的模块。删除后自动重建拓扑图。" +
        "注意：如果其他模块依赖此模块，删除后这些依赖会变为悬空引用（validate_architecture 会检测到）。")]
    public string delete_module(
        [Description("要删除的模块名")] string name)
    {
        logger.LogInformation(LogEvents.Mcp, "delete_module() name={Name}", name);

        if (string.IsNullOrWhiteSpace(name))
            return "错误：模块名不能为空";

        var ok = graph.UnregisterModule(name.Trim());
        if (!ok)
            return $"错误：未找到模块 '{name}'";

        graph.BuildTopology();
        return $"✓ 模块 '{name}' 已删除，拓扑已重建";
    }

    [McpServerTool, Description(
        "创建或修改一个 CrossWork（跨模块协作声明）。" +
        "CrossWork 定义了多个模块协作完成某个业务功能时，各自的角色、接口契约和交付物。" +
        "例如「角色换装」需要 Character（提供装备槽接口）+ Equipment（提供装备数据）+ UI（提供换装界面）三个模块协作。" +
        "参与者用 JSON 数组定义: [{\"moduleName\":\"Combat\",\"role\":\"伤害计算\",\"contract\":\"IDamageCalculator\",\"deliverable\":\"伤害数值\"}]。" +
        "如果同名 CrossWork 已存在则更新。")]
    public string register_crosswork(
        [Description("CrossWork 名称。示例: '角色换装', '战斗结算'")] string name,
        [Description("参与者 JSON 数组: [{\"moduleName\":\"模块名\",\"role\":\"职责\",\"contract\":\"接口\",\"deliverable\":\"交付物\"}]")] string participants,
        [Description("描述（可选）")] string? description = null,
        [Description("关联的业务系统 Feature（可选）。示例: 'character'")] string? feature = null)
    {
        logger.LogInformation(LogEvents.Mcp, "register_crosswork() name={Name}", name);

        if (string.IsNullOrWhiteSpace(name))
            return "错误：CrossWork 名称不能为空";

        List<CrossWorkParticipantRegistration>? parts;
        try { parts = JsonSerializer.Deserialize<List<CrossWorkParticipantRegistration>>(participants, JsonOpts); }
        catch (Exception ex) { return $"错误：participants 解析失败 — {ex.Message}"; }

        if (parts == null || parts.Count == 0)
            return "错误：至少需要一个参与者";

        parts = parts.Where(p => !string.IsNullOrWhiteSpace(p.ModuleName)).ToList();

        var crossWork = new CrossWorkRegistration
        {
            Name = name.Trim(),
            Description = description,
            Feature = feature,
            Participants = parts
        };

        graph.SaveCrossWork(crossWork);
        graph.BuildTopology();

        var partNames = string.Join(", ", parts.Select(p => $"{p.ModuleName}({p.Role})"));
        return $"✓ CrossWork '{name}' 已保存\n参与者: {partNames}";
    }

    [McpServerTool, Description(
        "删除一个 CrossWork 声明。删除后自动重建拓扑图。")]
    public string delete_crosswork(
        [Description("CrossWork 的 ID 或名称")] string id)
    {
        logger.LogInformation(LogEvents.Mcp, "delete_crosswork() id={Id}", id);

        if (string.IsNullOrWhiteSpace(id))
            return "错误：CrossWork ID 不能为空";

        var ok = graph.RemoveCrossWork(id.Trim());
        if (!ok)
            return $"错误：未找到 CrossWork '{id}'";

        graph.BuildTopology();
        return $"✓ CrossWork '{id}' 已删除，拓扑已重建";
    }

    // ═══════════════════════════════════════════
    //  内部
    // ═══════════════════════════════════════════

    private static string FormatModuleContext(ModuleContext ctx)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"## {ctx.ModuleName} ({ctx.Discipline ?? "generic"})");
        if (!string.IsNullOrWhiteSpace(ctx.Summary)) sb.AppendLine($"职责: {ctx.Summary}");
        if (!string.IsNullOrWhiteSpace(ctx.Boundary)) sb.AppendLine($"边界: {ctx.Boundary}");
        if (ctx.Constraints is { Count: > 0 }) sb.AppendLine($"约束: {string.Join("; ", ctx.Constraints)}");
        if (!string.IsNullOrWhiteSpace(ctx.IdentityContent)) sb.AppendLine($"\n{ctx.IdentityContent}");
        if (!string.IsNullOrWhiteSpace(ctx.LessonsContent)) sb.AppendLine($"\n{ctx.LessonsContent}");
        return sb.ToString();
    }

    private static List<string> SplitCsv(string csv) =>
        csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static double ScoreModule(KnowledgeNode module, string q)
    {
        var score = 0d;
        if (module.Name.Contains(q, StringComparison.OrdinalIgnoreCase)) score += 3.0;
        if (module.RelativePath?.Contains(q, StringComparison.OrdinalIgnoreCase) == true) score += 2.0;
        if (!string.IsNullOrWhiteSpace(module.Summary) &&
            module.Summary.Contains(q, StringComparison.OrdinalIgnoreCase)) score += 1.5;
        if (module.Keywords.Any(k => k.Contains(q, StringComparison.OrdinalIgnoreCase))) score += 1.2;
        if (module.Dependencies.Any(d => d.Contains(q, StringComparison.OrdinalIgnoreCase))) score += 0.5;
        if (module.ComputedDependencies.Any(d => d.Contains(q, StringComparison.OrdinalIgnoreCase))) score += 0.5;
        return score;
    }

    private static void AppendDeps(StringBuilder sb, KnowledgeNode n)
    {
        var allDeps = n.Dependencies.Union(n.ComputedDependencies, StringComparer.OrdinalIgnoreCase).OrderBy(s => s).ToList();
        if (allDeps.Count == 0) return;

        var declared = new HashSet<string>(n.Dependencies, StringComparer.OrdinalIgnoreCase);
        var computed = new HashSet<string>(n.ComputedDependencies, StringComparer.OrdinalIgnoreCase);

        var parts = allDeps.Select(d =>
        {
            var inD = declared.Contains(d);
            var inC = computed.Contains(d);
            return (inD, inC) switch
            {
                (true, true) => d,
                (true, false) => $"{d}(declared)",
                (false, true) => $"{d}(computed)",
                _ => d
            };
        });
        sb.AppendLine($"  deps: [{string.Join(", ", parts)}]");
    }
}
