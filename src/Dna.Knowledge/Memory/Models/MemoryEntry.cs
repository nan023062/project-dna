using Dna.Knowledge;

namespace Dna.Memory.Models;

/// <summary>
/// 记忆条目 — 知识图谱的最小单元。
/// 通过知识坐标（NodeType × Discipline × Feature × Path）定位在项目中的位置，
/// 通过约束关系（ParentId / RelatedIds）表达层级约束链。
/// </summary>
public sealed class MemoryEntry
{
    private MemoryContent _contentData = new();
    private MemoryAddress _address = new();
    private MemoryLifecycle _lifecycle = new();

    // === 唯一标识 ===

    /// <summary>ULID — 天然时间有序，兼容字符串排序</summary>
    public string Id { get; init; } = string.Empty;

    // === 认知维度 ===

    public MemoryType Type { get; init; }
    public MemorySource Source { get; init; }
    public MemoryContent ContentData
    {
        get => _contentData;
        init => _contentData = value ?? new MemoryContent();
    }
    public MemoryAddress Address
    {
        get => _address;
        init => _address = value ?? new MemoryAddress();
    }
    public MemoryLifecycle Lifecycle
    {
        get => _lifecycle;
        init => _lifecycle = value ?? new MemoryLifecycle();
    }

    // === 知识坐标 ===

    public NodeType NodeType
    {
        get => _address.NodeType;
        init => _address.NodeType = value;
    }

    /// <summary>关联的职能域：engineering / design / art / ta / audio / devops / qa</summary>
    public List<string> Disciplines
    {
        get => _address.Disciplines;
        init => _address.Disciplines = value ?? [];
    }

    /// <summary>关联的业务系统：character / building / fishing / social …</summary>
    public List<string> Features
    {
        get => _address.Features;
        init => _address.Features = value ?? [];
    }

    /// <summary>所属节点 ID（一对一），解耦物理路径</summary>
    public string? NodeId
    {
        get => _address.NodeId;
        init => _address.NodeId = value;
    }

    /// <summary>关联的文件路径模式（glob），用于鲜活度检测和路径匹配</summary>
    public List<string> PathPatterns
    {
        get => _address.PathPatterns;
        init => _address.PathPatterns = value ?? [];
    }

    // === 约束关系 ===

    /// <summary>上层约束来源（父记忆 ID），用于约束链展开</summary>
    public string? ParentId
    {
        get => _address.ParentMemoryId;
        init => _address.ParentMemoryId = value;
    }

    /// <summary>关联记忆 ID 列表</summary>
    public List<string> RelatedIds
    {
        get => _address.RelatedMemoryIds;
        init => _address.RelatedMemoryIds = value ?? [];
    }

    // === 内容 ===

    /// <summary>知识正文（Markdown 格式）</summary>
    public string Content
    {
        get => _contentData.Body;
        init => _contentData.Body = value ?? string.Empty;
    }

    /// <summary>一句话摘要，用于列表展示和快速索引</summary>
    public string? Summary
    {
        get => _contentData.Summary;
        init => _contentData.Summary = value;
    }

    /// <summary>辅助标签：#lesson #changelog #tech-debt #needs-review …</summary>
    public List<string> Tags
    {
        get => _contentData.Tags;
        init => _contentData.Tags = value ?? [];
    }

    /// <summary>重要度 0.0-1.0，影响召回排序</summary>
    public double Importance
    {
        get => _contentData.Importance;
        init => _contentData.Importance = value;
    }

    // === 时效性 ===

    public MemoryStage Stage
    {
        get => _lifecycle.Stage;
        init => _lifecycle.Stage = value;
    }
    public FreshnessStatus Freshness
    {
        get => _lifecycle.Freshness;
        set => _lifecycle.Freshness = value;
    }
    public DateTime CreatedAt
    {
        get => _lifecycle.CreatedAt;
        init => _lifecycle.CreatedAt = value;
    }

    /// <summary>上次验证仍有效的时间</summary>
    public DateTime? LastVerifiedAt
    {
        get => _lifecycle.LastVerifiedAt;
        set => _lifecycle.LastVerifiedAt = value;
    }

    /// <summary>预计过时时间（超过后自动标记 Aging）</summary>
    public DateTime? StaleAfter
    {
        get => _lifecycle.StaleAfter;
        init => _lifecycle.StaleAfter = value;
    }

    /// <summary>被哪条新知识取代（指向 superseding 记忆的 ID）</summary>
    public string? SupersededBy
    {
        get => _lifecycle.SupersededBy;
        set => _lifecycle.SupersededBy = value;
    }

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
    public int Version
    {
        get => _lifecycle.Version;
        init => _lifecycle.Version = value;
    }
}
