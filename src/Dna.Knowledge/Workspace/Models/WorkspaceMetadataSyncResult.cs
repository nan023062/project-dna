namespace Dna.Knowledge.Workspace.Models;

public sealed class WorkspaceMetadataSyncResult
{
    public string RootRelativePath { get; init; } = string.Empty;
    public int ProcessedDirectoryCount { get; init; }
    public int CreatedMetadataCount { get; init; }
}
