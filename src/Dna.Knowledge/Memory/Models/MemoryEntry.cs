using Dna.Knowledge;

namespace Dna.Memory.Models;

/// <summary>
/// 记忆条目 — 知识图谱的最小单元。
/// 通过知识坐标（NodeType × Discipline × Feature × Path）定位在项目中的位置，
/// 通过约束关系（ParentId / RelatedIds）表达层级约束链。
/// </summary>
public sealed class MemoryEntry
{
    // === 唯一标识 ===

    /// <summary>ULID — 天然时间有序，兼容字符串排序</summary>
    public string Id { get; init; } = string.Empty;

    // === 认知维度 ===

    public MemoryType Type { get; init; }
    public MemorySource Source { get; init; }

    // === 知识坐标 ===

    public NodeType NodeType { get; init; } = NodeType.Group;

    /// <summary>关联的职能域：engineering / design / art / ta / audio / devops / qa</summary>
    public List<string> Disciplines { get; init; } = [];

    /// <summary>关联的业务系统：character / building / fishing / social …</summary>
    public List<string> Features { get; init; } = [];

    /// <summary>所属节点 ID（一对一），解耦物理路径</summary>
    public string? NodeId { get; init; }

    /// <summary>关联的文件路径模式（glob），用于鲜活度检测和路径匹配</summary>
    public List<string> PathPatterns { get; init; } = [];

    // === 约束关系 ===

    /// <summary>上层约束来源（父记忆 ID），用于约束链展开</summary>
    public string? ParentId { get; init; }

    /// <summary>关联记忆 ID 列表</summary>
    public List<string> RelatedIds { get; init; } = [];

    // === 内容 ===

    /// <summary>知识正文（Markdown 格式）</summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>一句话摘要，用于列表展示和快速索引</summary>
    public string? Summary { get; init; }

    /// <summary>辅助标签：#lesson #changelog #tech-debt #needs-review …</summary>
    public List<string> Tags { get; init; } = [];

    /// <summary>重要度 0.0-1.0，影响召回排序</summary>
    public double Importance { get; init; } = 0.5;

    // === 时效性 ===

    public FreshnessStatus Freshness { get; set; } = FreshnessStatus.Fresh;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>上次验证仍有效的时间</summary>
    public DateTime? LastVerifiedAt { get; set; }

    /// <summary>预计过时时间（超过后自动标记 Aging）</summary>
    public DateTime? StaleAfter { get; init; }

    /// <summary>被哪条新知识取代（指向 superseding 记忆的 ID）</summary>
    public string? SupersededBy { get; set; }

    /// <summary>知识演化链（前驱记忆 ID 列表）</summary>
    public List<string> EvolutionChain { get; init; } = [];

    // === 外部来源追溯 ===

    /// <summary>Confluence 页面 URL</summary>
    public string? ExternalSourceUrl { get; init; }

    /// <summary>JIRA Issue Key 等外部 ID</summary>
    public string? ExternalSourceId { get; init; }

    public DateTime? ExternalLastModified { get; init; }

    // === 向量 ===

    /// <summary>语义嵌入向量（由 EmbeddingService 生成）</summary>
    public float[]? Embedding { get; set; }

    /// <summary>条目版本号，每次 Update 递增</summary>
    public int Version { get; init; } = 1;
}
