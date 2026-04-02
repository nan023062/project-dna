namespace Dna.Knowledge.Workspace.Models;

public sealed class WorkspaceChangeSet
{
    public string ProjectRoot { get; init; } = string.Empty;
    public DateTime OccurredAtUtc { get; init; }
    public List<WorkspaceChangeEntry> Entries { get; init; } = [];
}

public sealed class WorkspaceChangeEntry
{
    public WorkspaceChangeKind Kind { get; init; }
    public WorkspaceChangeTargetKind TargetKind { get; init; } = WorkspaceChangeTargetKind.Unknown;
    public string Path { get; init; } = string.Empty;
    public string ParentPath { get; init; } = string.Empty;
    public string? PreviousPath { get; init; }
}

public enum WorkspaceChangeKind
{
    Created,
    Changed,
    Deleted,
    Renamed,
    Invalidated,
    Reset
}

public enum WorkspaceChangeTargetKind
{
    Unknown,
    File,
    Directory
}
