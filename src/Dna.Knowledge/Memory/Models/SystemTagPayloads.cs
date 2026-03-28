namespace Dna.Memory.Models;

/// <summary>
/// 系统保留标签常量。保留标签的 content 必须是 JSON。
/// </summary>
public static class WellKnownTags
{
    public const string Identity = "#identity";
    public const string Lesson = "#lesson";
    public const string ActiveTask = "#active-task";
}

public sealed class IdentityPayload
{
    public string Summary { get; set; } = string.Empty;
    public string? Contract { get; set; }
    public List<string> Keywords { get; set; } = [];
    public string? Description { get; set; }
}

public sealed class LessonPayload
{
    public string Title { get; set; } = string.Empty;
    public string? Severity { get; set; }
    public string Context { get; set; } = string.Empty;
    public string? Resolution { get; set; }
    public List<string> Tags { get; set; } = [];
}

public sealed class ActiveTaskPayload
{
    public string Task { get; set; } = string.Empty;
    public string? Status { get; set; }
    public string? Assignee { get; set; }
    public List<string> RelatedModules { get; set; } = [];
    public string? Notes { get; set; }
}
