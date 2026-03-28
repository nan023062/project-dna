using Dna.Knowledge.Models;

namespace Dna.Knowledge.Project.Models;

/// <summary>
/// architecture.json 的根结构 — 项目组织架构定义（极少变更）。
/// 定义部门和每个部门的层级结构，模块注册时必须从中选择。
/// </summary>
public sealed class ArchitectureManifest
{
    public Dictionary<string, DisciplineDefinition> Disciplines { get; set; } = new();

    /// <summary>扫描时额外排除的目录名（追加到默认排除列表）</summary>
    public List<string>? ExcludeDirs { get; set; }

    /// <summary>项目拟人化配置 — MCP 服务对外呈现的名称和人格。</summary>
    public PersonaConfig? Persona { get; set; }
}

/// <summary>
/// 拟人化配置 — 让知识库以角色身份与团队成员交互。
/// 存储在 architecture.json 中，通过 Dashboard 或 MCP 设置。
/// </summary>
public sealed class PersonaConfig
{
    /// <summary>全名，如"小镇百事通"。用于 MCP serverInfo.name。</summary>
    public string Name { get; set; } = "Project DNA";

    /// <summary>简称，如"小镇通"。用于 Cursor Rules 中的简短称呼。</summary>
    public string? ShortName { get; set; }

    /// <summary>自我介绍，如"我是小镇百事通，你们项目的知识管家"。</summary>
    public string? Description { get; set; }

    /// <summary>打招呼语，如"有什么想问的？我知道这个项目的所有规范和历史。"</summary>
    public string? Greeting { get; set; }
}

/// <summary>
/// 部门定义（架构层面）：显示名 + 预定义层级。
/// </summary>
public sealed class DisciplineDefinition
{
    public string? DisplayName { get; set; }

    /// <summary>工种 ID（对应 IProjectAdapter 中的 Interpreter，如 coder/designer/art）</summary>
    public string RoleId { get; set; } = "coder";

    public List<LayerDefinition> Layers { get; set; } = [];
}

/// <summary>
/// 默认排除目录列表。
/// </summary>
public static class DefaultExcludes
{
    public static readonly HashSet<string> Dirs = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "node_modules", "bin", "obj", ".dna", ".vs", ".idea",
        "Temp", "Library", "Logs", "logs", "__pycache__", ".vscode", ".cursor",
        "publish", "Publish", ".gradle", "build", "dist", "out"
    };

    public static HashSet<string> BuildWithCustom(IEnumerable<string>? customDirs)
    {
        var merged = new HashSet<string>(Dirs, StringComparer.OrdinalIgnoreCase);
        if (customDirs == null) return merged;

        foreach (var dir in customDirs)
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            merged.Add(dir.Trim());
        }

        return merged;
    }
}
