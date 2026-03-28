namespace Dna.Memory.Models;

/// <summary>
/// 五层知识层级 — 每一层约束下一层。
/// AI 执行 L4 任务时，系统自动召回 L0-L3 的约束。
/// </summary>
public enum KnowledgeLayer
{
    /// <summary>L0: 项目愿景 — 游戏类型、核心体验、目标平台、技术选型</summary>
    ProjectVision = 0,

    /// <summary>L1: 职能总纲 — 各部门的技术框架、质量标准、规范</summary>
    DisciplineStandard = 1,

    /// <summary>L2: 跨职能协议 — 部门间的输入/输出约定、工具工作流</summary>
    CrossDiscipline = 2,

    /// <summary>L3: 业务系统 — 玩法/功能的多职能设计与实现知识</summary>
    FeatureSystem = 3,

    /// <summary>L4: 执行细节 — 教训、变更记录、调试经验、具体实现</summary>
    Implementation = 4
}
