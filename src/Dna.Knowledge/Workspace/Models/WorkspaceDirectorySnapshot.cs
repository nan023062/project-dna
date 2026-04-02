namespace Dna.Knowledge.Workspace.Models;

public sealed class WorkspaceDirectorySnapshot
{
    public string ProjectRoot { get; init; } = string.Empty;
    public string RelativePath { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public bool Exists { get; init; }
    public DateTime ScannedAtUtc { get; init; }
    public int DirectoryCount { get; init; }
    public int FileCount { get; init; }
    public List<WorkspaceFileNode> Entries { get; init; } = [];

    public WorkspaceDirectorySnapshot Clone()
    {
        return new WorkspaceDirectorySnapshot
        {
            ProjectRoot = ProjectRoot,
            RelativePath = RelativePath,
            Name = Name,
            FullPath = FullPath,
            Exists = Exists,
            ScannedAtUtc = ScannedAtUtc,
            DirectoryCount = DirectoryCount,
            FileCount = FileCount,
            Entries = Entries.Select(static entry => entry.Clone()).ToList()
        };
    }
}
