namespace Dna.Knowledge.Models;

/// <summary>
/// modules.json 的根结构 — 模块注册与业务协作数据（频繁变更）。
/// </summary>
public sealed class ModulesManifest
{
    /// <summary>按职能域组织的模块列表（key = discipline id）</summary>
    public Dictionary<string, List<ModuleRegistration>> Disciplines { get; set; } = new();

    /// <summary>业务交叉工作声明</summary>
    public List<CrossWorkRegistration> CrossWorks { get; set; } = [];

    /// <summary>按业务系统组织的跨职能特性</summary>
    public Dictionary<string, FeatureDefinition> Features { get; set; } = new();
}

/// <summary>
/// 层级定义（声明式）：由 discipline 预定义，模块注册时选择。
/// </summary>
public sealed class LayerDefinition
{
    public int Level { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// 模块注册信息 — modules.json 中的最小模块描述。
/// 当 IsCrossWorkModule=true 时，该模块是"跨模块工作组"：
///   - 可访问任意非 CrossWork 模块（免依赖校验）
///   - 不被任何其他模块依赖（违规）
///   - CrossWork 模块之间互相隔离（违规）
///   - Participants 描述工作组成员及其职责/契约
/// </summary>
public sealed class ModuleRegistration
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public int Layer { get; set; }

    /// <summary>是否为 CrossWork 工作组模块（特殊模块：跨模块协议/方案的承载体）。</summary>
    public bool IsCrossWorkModule { get; set; }

    /// <summary>CrossWork 工作组成员（仅 IsCrossWorkModule=true 时有效）。</summary>
    public List<CrossWorkParticipantRegistration> Participants { get; set; } = [];

    public List<string> Dependencies { get; set; } = [];
    public string? Maintainer { get; set; }

    /// <summary>模块职责一句话描述。</summary>
    public string? Summary { get; set; }

    /// <summary>边界模式：open（任何人可调用）/ semi-open（通过指定接口调用）/ closed（仅内部使用）。</summary>
    public string? Boundary { get; set; }

    /// <summary>对外暴露的公开接口/API 列表。</summary>
    public List<string>? PublicApi { get; set; }

    /// <summary>模块约束规则（不可做的事）。</summary>
    public List<string>? Constraints { get; set; }

    /// <summary>自定义扩展属性（key-value），项目可自由定义字段如 performanceBudget、gcPolicy 等。</summary>
    public Dictionary<string, string>? Metadata { get; set; }
}

/// <summary>
/// 业务系统定义 — 横跨多个职能的功能单元。
/// </summary>
public sealed class FeatureDefinition
{
    public List<string> Disciplines { get; set; } = [];
    public List<string> Paths { get; set; } = [];
}

/// <summary>
/// CrossWork 的结构化注册信息。
/// </summary>
public sealed class CrossWorkRegistration
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Feature { get; set; }
    public List<CrossWorkParticipantRegistration> Participants { get; set; } = [];
}

public sealed class CrossWorkParticipantRegistration
{
    public string ModuleName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string? ContractType { get; set; }
    public string? Contract { get; set; }
    public string? Deliverable { get; set; }
}

/// <summary>
/// modules.computed.json — 机器扫描的事实快照。
/// </summary>
public sealed class ComputedManifest
{
    public Dictionary<string, List<string>> ModuleDependencies { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
