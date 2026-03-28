namespace Dna.Knowledge;

/// <summary>
/// 业务交叉工作 — 多模块（可跨层、跨职能）协同交付一个业务目标。
/// 参与模块之间无直接依赖，各自按契约独立贡献。
/// CrossWork 不产生拓扑 Edge，不影响分层。
/// </summary>
public class CrossWork
{
    public string Id { get; init; } = string.Empty;

    /// <summary>业务目标名称（如 "火球术技能"、"角色换装"）</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>业务描述</summary>
    public string? Description { get; init; }

    /// <summary>关联的业务系统 / Feature（如 "combat"、"character"）</summary>
    public string? Feature { get; init; }

    /// <summary>参与模块列表（2 个或更多，可跨层跨职能）</summary>
    public List<CrossWorkParticipant> Participants { get; init; } = [];
}

/// <summary>
/// CrossWork 中的单个参与方 — 描述该模块在协作中的职责与契约。
/// </summary>
public class CrossWorkParticipant
{
    /// <summary>模块名</summary>
    public string ModuleName { get; init; } = string.Empty;

    /// <summary>该模块在此 CrossWork 中的职责</summary>
    public string Role { get; init; } = string.Empty;

    /// <summary>该模块为此 CrossWork 暴露的接口/资产契约</summary>
    public string? Contract { get; init; }

    /// <summary>契约类型（如 CodeInterface, AssetSocket, DataForeignKey）</summary>
    public string? ContractType { get; init; }

    /// <summary>交付物描述（代码接口？配置表？资产文件？）</summary>
    public string? Deliverable { get; init; }
}
