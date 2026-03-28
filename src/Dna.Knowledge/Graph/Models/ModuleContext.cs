namespace Dna.Knowledge;

/// <summary>
/// 模块上下文 — 按视界过滤后的模块知识，供 AI Agent 消费。
/// 内容从 MemoryStore 中的记忆条目组装而来。
/// </summary>
public class ModuleContext
{
    public string ModuleName { get; init; } = string.Empty;
    public string? Discipline { get; init; }
    public ContextLevel Level { get; init; }

    /// <summary>模块身份描述（identity 记忆）</summary>
    public string? IdentityContent { get; init; }

    /// <summary>教训记录</summary>
    public string? LessonsContent { get; init; }

    /// <summary>依赖声明</summary>
    public string? LinksContent { get; init; }

    /// <summary>当前任务</summary>
    public string? ActiveContent { get; init; }

    /// <summary>Contract 段（CrossWorkPeer 时仅返回此字段）</summary>
    public string? ContractContent { get; init; }

    /// <summary>模块目录下的文件路径列表</summary>
    public List<string> ContentFilePaths { get; init; } = [];

    /// <summary>阻断消息（Unlinked 时填充）</summary>
    public string? BlockMessage { get; init; }

    // ── 结构化模块属性（来自 ModuleRegistration，权威描述） ──

    /// <summary>模块职责摘要</summary>
    public string? Summary { get; init; }

    /// <summary>边界模式：open / semi-open / closed</summary>
    public string? Boundary { get; init; }

    /// <summary>对外接口列表</summary>
    public List<string>? PublicApi { get; init; }

    /// <summary>约束规则</summary>
    public List<string>? Constraints { get; init; }

    /// <summary>自定义扩展属性</summary>
    public Dictionary<string, string>? Metadata { get; init; }

    public bool IsBlocked => Level == ContextLevel.Unlinked;
}
