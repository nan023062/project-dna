namespace Dna.Memory.Models;

/// <summary>
/// 记忆鲜活度状态 — 知识的生命周期管理
/// </summary>
public enum FreshnessStatus
{
    /// <summary>新鲜：最近创建或验证过</summary>
    Fresh,

    /// <summary>老化：超过预期保鲜期，但未确认过时</summary>
    Aging,

    /// <summary>陈旧：有迹象表明已过时（关联代码已变）</summary>
    Stale,

    /// <summary>已取代：有明确的新版知识替代</summary>
    Superseded,

    /// <summary>归档：仅作历史参考，不参与主动召回</summary>
    Archived
}
