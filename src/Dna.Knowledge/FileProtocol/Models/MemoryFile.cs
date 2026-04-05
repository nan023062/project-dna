namespace Dna.Knowledge.FileProtocol.Models;

/// <summary>
/// Memory 文件的结构化表示（YAML frontmatter + Markdown body）。
/// </summary>
public sealed class MemoryFile
{
    // === frontmatter 字段 ===

    /// <summary>ULID，与文件名一致</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Structural / Semantic / Episodic / Working / Procedural</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Human / Ai / System / External</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>归属模块 UID</summary>
    public string? NodeId { get; set; }

    /// <summary>所属领域</summary>
    public List<string>? Disciplines { get; set; }

    /// <summary>标签列表</summary>
    public List<string>? Tags { get; set; }

    /// <summary>重要度 0.0-1.0</summary>
    public double? Importance { get; set; }

    /// <summary>创建时间</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>最后验证时间</summary>
    public DateTime? LastVerifiedAt { get; set; }

    /// <summary>被替代的新记忆 ID</summary>
    public string? SupersededBy { get; set; }

    // === 正文 ===

    /// <summary>Markdown 正文内容</summary>
    public string Body { get; set; } = string.Empty;

    // === 文件位置 ===

    /// <summary>memory 分类目录（decisions / lessons / conventions / summaries）</summary>
    public string? Category { get; set; }
}
