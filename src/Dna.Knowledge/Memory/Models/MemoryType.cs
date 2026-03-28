namespace Dna.Memory.Models;

/// <summary>
/// 记忆的认知类型 — 对应人类记忆的五种分类
/// </summary>
public enum MemoryType
{
    /// <summary>结构记忆 — 拓扑、Contract、注册信息</summary>
    Structural,

    /// <summary>语义记忆 — 决策、约定、规范、标准</summary>
    Semantic,

    /// <summary>事件记忆 — 教训、变更、反馈、里程碑</summary>
    Episodic,

    /// <summary>工作记忆 — 当前任务、调用栈</summary>
    Working,

    /// <summary>过程记忆 — 操作流程、checklist、最佳实践</summary>
    Procedural
}
