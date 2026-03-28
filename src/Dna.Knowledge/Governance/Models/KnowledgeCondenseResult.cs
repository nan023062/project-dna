namespace Dna.Knowledge;

/// <summary>
/// 模块知识压缩结果
/// </summary>
public sealed class KnowledgeCondenseResult
{
    public string NodeId { get; init; } = string.Empty;
    public string? NodeName { get; init; }
    public int SourceCount { get; init; }
    public int ArchivedCount { get; init; }
    public string? NewIdentityMemoryId { get; init; }
    public string? Summary { get; init; }
}

