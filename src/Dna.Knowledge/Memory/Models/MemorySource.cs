namespace Dna.Memory.Models;

/// <summary>
/// 记忆的来源 — 标记知识是从哪里产生的
/// </summary>
public enum MemorySource
{
    /// <summary>系统自动生成（拓扑推断、健康检查）</summary>
    System,

    /// <summary>AI 工作过程中产生</summary>
    Ai,

    /// <summary>人工显式记录</summary>
    Human,

    /// <summary>外部系统（CI、QA、Code Review）</summary>
    External,

    /// <summary>从 Confluence 蒸馏导入</summary>
    Confluence,

    /// <summary>从 JIRA 事件导入</summary>
    Jira
}
