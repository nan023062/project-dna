namespace Dna.Memory.Models;

/// <summary>
/// 已废弃：记忆坐标已迁移到 NodeType（Project/Department/Group/Team）。
/// 仅保留此枚举用于历史兼容，请勿在新代码中使用。
/// </summary>
[Obsolete("KnowledgeLayer 已废弃，请改用 NodeType。")]
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
