using Dna.Knowledge;

namespace Dna.Memory.Models;

/// <summary>
/// remember 写入请求
/// </summary>
public sealed class RememberRequest
{
    public MemoryType Type { get; init; }
    public NodeType? NodeType { get; init; }
    public MemorySource Source { get; init; } = MemorySource.Ai;
    public string Content { get; init; } = string.Empty;
    public string? Summary { get; init; }
    public List<string> Disciplines { get; init; } = [];
    public List<string>? Features { get; init; }
    public string? NodeId { get; init; }
    public List<string>? PathPatterns { get; init; }
    public List<string> Tags { get; init; } = [];
    public string? ParentId { get; init; }
    public MemoryStage? Stage { get; init; }
    public double Importance { get; init; } = 0.5;
    public string? ExternalSourceUrl { get; init; }
    public string? ExternalSourceId { get; init; }

    public NodeType ResolvedNodeType => NodeType ?? Dna.Knowledge.NodeType.Technical;
}

/// <summary>
/// recall 检索查询
/// </summary>
public sealed class RecallQuery
{
    public string Question { get; init; } = string.Empty;
    public List<NodeType>? NodeTypes { get; init; }
    public List<string>? Disciplines { get; init; }
    public List<string>? Features { get; init; }
    public List<MemoryType>? Types { get; init; }
    public List<string>? Tags { get; init; }
    public string? NodeId { get; init; }
    public List<string>? PathPatterns { get; init; }
    public FreshnessFilter Freshness { get; init; } = FreshnessFilter.FreshAndAging;
    public bool ExpandConstraintChain { get; init; } = true;
    public int MaxResults { get; init; } = 10;

    public List<NodeType>? ResolvedNodeTypes => NodeTypes?.Distinct().ToList();
}

public enum FreshnessFilter
{
    FreshOnly,
    FreshAndAging,
    IncludeStale,
    IncludeArchived,
    All
}

/// <summary>
/// recall 检索结果
/// </summary>
public sealed class RecallResult
{
    public List<ScoredMemory> Memories { get; init; } = [];
    public List<MemoryEntry> ConstraintChain { get; init; } = [];
    public double Confidence { get; init; }
    public List<string> SuggestedFollowUps { get; init; } = [];
    public bool IsVectorDegraded { get; init; }
}

public sealed class ScoredMemory
{
    public MemoryEntry Entry { get; init; } = null!;
    public double Score { get; set; }
    public string MatchChannel { get; init; } = string.Empty;
}

/// <summary>
/// 结构化查询过滤器（用于 Query 接口，非语义检索）
/// </summary>
public sealed class MemoryFilter
{
    public List<NodeType>? NodeTypes { get; init; }
    public List<string>? Disciplines { get; init; }
    public List<string>? Features { get; init; }
    public List<MemoryType>? Types { get; init; }
    public List<string>? Tags { get; init; }
    public string? NodeId { get; init; }
    public FreshnessFilter Freshness { get; init; } = FreshnessFilter.FreshAndAging;
    public int Limit { get; init; } = 50;
    public int Offset { get; init; }

    public List<NodeType>? ResolvedNodeTypes => NodeTypes?.Distinct().ToList();
}

/// <summary>
/// 业务系统知识汇总
/// </summary>
public sealed class FeatureKnowledgeSummary
{
    public string FeatureId { get; init; } = string.Empty;
    public Dictionary<string, List<MemoryEntry>> ByDiscipline { get; init; } = new();
    public List<MemoryEntry> CrossDiscipline { get; init; } = [];
    public int TotalCount { get; init; }
}

/// <summary>
/// 职能知识汇总
/// </summary>
public sealed class DisciplineKnowledgeSummary
{
    public string DisciplineId { get; init; } = string.Empty;
    public Dictionary<NodeType, List<MemoryEntry>> ByNodeType { get; init; } = new();
    public int TotalCount { get; init; }
}

/// <summary>
/// 记忆统计
/// </summary>
public sealed class MemoryStats
{
    public int Total { get; init; }
    public int ConflictCount { get; init; }
    public Dictionary<string, int> ByNodeType { get; init; } = new();
    public Dictionary<string, int> ByDiscipline { get; init; } = new();
    public Dictionary<string, int> ByFeature { get; init; } = new();
    public Dictionary<string, int> ByFreshness { get; init; } = new();
    public Dictionary<string, int> ByType { get; init; } = new();
}
