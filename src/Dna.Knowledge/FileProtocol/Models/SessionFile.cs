namespace Dna.Knowledge.FileProtocol.Models;

/// <summary>
/// Session 文件的结构化表示，使用 YAML frontmatter + Markdown body。
/// </summary>
public sealed class SessionFile
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string? NodeId { get; set; }
    public List<string>? Tags { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Body { get; set; } = string.Empty;
    public string? Category { get; set; }
}
