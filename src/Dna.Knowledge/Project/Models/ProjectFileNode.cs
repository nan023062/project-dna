namespace Dna.Knowledge.Project.Models;

/// <summary>
/// 工程文件树节点 — 扫描结果的结构化表示。
/// 包含状态、模块信息、可用操作，前端只做渲染不做逻辑判断。
/// </summary>
public class ProjectFileNode
{
    public string Name { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public FileNodeStatus Status { get; set; } = FileNodeStatus.Candidate;
    public string StatusLabel { get; set; } = "候选";
    public string? Badge { get; set; }
    public FileNodeModuleInfo? Module { get; set; }
    public FileNodeActions Actions { get; set; } = new();

    /// <summary>是否有子目录（用于前端显示展开箭头）</summary>
    public bool HasChildren { get; set; }

    /// <summary>子目录（懒加载：首次为 null，展开时填充）</summary>
    public List<ProjectFileNode>? Children { get; set; }
}

public enum FileNodeStatus
{
    Registered,
    CrossWork,
    Container,
    Candidate
}

public class FileNodeActions
{
    public bool CanRegister { get; init; }
    public bool CanEdit { get; init; }
    public string? SuggestedDiscipline { get; init; }
    public int? SuggestedLayer { get; init; }
}

public class FileNodeModuleInfo
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Discipline { get; init; } = string.Empty;
    public int Layer { get; init; }
    public bool IsCrossWorkModule { get; init; }
}
