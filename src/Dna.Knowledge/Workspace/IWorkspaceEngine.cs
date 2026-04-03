using Dna.Knowledge.Workspace.Models;
using System.Text;

namespace Dna.Knowledge.Workspace;

public interface IWorkspaceEngine
{
    event EventHandler<WorkspaceChangeSet>? Changed;

    void Initialize(string projectRoot, WorkspaceTopologyContext topology);

    string ResolveFullPath(string projectRoot, string relativePath);
    string ResolveMetadataFilePath(string projectRoot, string directoryRelativePath);

    WorkspaceDirectorySnapshot GetRootSnapshot(
        string projectRoot,
        WorkspaceTopologyContext topology);

    WorkspaceDirectorySnapshot GetDirectorySnapshot(
        string projectRoot,
        string relativePath,
        WorkspaceTopologyContext topology);

    WorkspaceFileNode? TryGetEntry(
        string projectRoot,
        string relativePath,
        WorkspaceTopologyContext topology);

    List<WorkspaceFileNode> GetRoots(
        string projectRoot,
        WorkspaceTopologyContext topology);

    List<WorkspaceFileNode> GetChildren(
        string projectRoot,
        string relativePath,
        WorkspaceTopologyContext topology);

    Task<string> ReadTextAsync(
        string projectRoot,
        string relativePath,
        Encoding? encoding = null,
        CancellationToken cancellationToken = default);

    Task<byte[]> ReadBytesAsync(
        string projectRoot,
        string relativePath,
        CancellationToken cancellationToken = default);

    Task WriteTextAsync(
        string projectRoot,
        string relativePath,
        string content,
        Encoding? encoding = null,
        CancellationToken cancellationToken = default);

    Task WriteBytesAsync(
        string projectRoot,
        string relativePath,
        byte[] content,
        CancellationToken cancellationToken = default);

    WorkspaceDirectoryMetadataDocument? TryReadDirectoryMetadata(
        string projectRoot,
        string directoryRelativePath);

    Task<WorkspaceDirectoryMetadataDocument> EnsureDirectoryMetadataAsync(
        string projectRoot,
        string directoryRelativePath,
        CancellationToken cancellationToken = default);

    Task<WorkspaceDirectoryMetadataDocument> WriteDirectoryMetadataAsync(
        string projectRoot,
        string directoryRelativePath,
        WorkspaceDirectoryMetadataDocument document,
        CancellationToken cancellationToken = default);

    Task<WorkspaceMetadataSyncResult> EnsureDirectoryMetadataTreeAsync(
        string projectRoot,
        WorkspaceTopologyContext topology,
        string relativePath = "",
        CancellationToken cancellationToken = default);

    void EnsureDirectory(
        string projectRoot,
        string relativePath);

    bool DeleteEntry(
        string projectRoot,
        string relativePath,
        bool recursive = false);

    void Invalidate(string relativePath);

    void InvalidateAll();
}
