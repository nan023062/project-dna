using Dna.Knowledge.Workspace.Models;
using System.Text;

namespace Dna.Knowledge.Workspace;

/// <summary>
/// Workspace engine: exposes stable workspace facts, path mapping and basic file IO for upper layers.
/// </summary>
public sealed class WorkspaceEngine(WorkspaceTreeCache treeCache) : IWorkspaceEngine, IDisposable
{
    public event EventHandler<WorkspaceChangeSet>? Changed
    {
        add => treeCache.Changed += value;
        remove => treeCache.Changed -= value;
    }

    public void Initialize(string projectRoot, WorkspaceTopologyContext topology)
    {
        treeCache.Initialize(projectRoot, topology);
    }

    public string ResolveFullPath(string projectRoot, string relativePath)
    {
        return WorkspacePath.ResolveFullPathWithinRoot(projectRoot, relativePath);
    }

    public string ResolveMetadataFilePath(string projectRoot, string directoryRelativePath)
    {
        return WorkspaceMetadataFile.GetMetadataFilePath(projectRoot, directoryRelativePath);
    }

    public WorkspaceDirectorySnapshot GetRootSnapshot(
        string projectRoot,
        WorkspaceTopologyContext topology)
    {
        return GetDirectorySnapshot(projectRoot, string.Empty, topology);
    }

    public WorkspaceDirectorySnapshot GetDirectorySnapshot(
        string projectRoot,
        string relativePath,
        WorkspaceTopologyContext topology)
    {
        Initialize(projectRoot, topology);
        var normalizedPath = WorkspacePath.NormalizeRelativePath(relativePath);

        return treeCache.GetDirectorySnapshot(
            normalizedPath,
            (root, path) => WorkspaceScanner.ScanDirectory(root, path, topology));
    }

    public WorkspaceFileNode? TryGetEntry(
        string projectRoot,
        string relativePath,
        WorkspaceTopologyContext topology)
    {
        var normalizedPath = WorkspacePath.NormalizeRelativePath(relativePath);
        if (normalizedPath.Length == 0)
            return null;

        var parentPath = WorkspacePath.GetParentPath(normalizedPath);
        var snapshot = GetDirectorySnapshot(projectRoot, parentPath, topology);
        return snapshot.Entries.FirstOrDefault(entry =>
            string.Equals(entry.Path, normalizedPath, StringComparison.OrdinalIgnoreCase));
    }

    public List<WorkspaceFileNode> GetRoots(
        string projectRoot,
        WorkspaceTopologyContext topology)
    {
        return GetRootSnapshot(projectRoot, topology).Entries
            .Select(static entry => entry.Clone())
            .ToList();
    }

    public List<WorkspaceFileNode> GetChildren(
        string projectRoot,
        string relativePath,
        WorkspaceTopologyContext topology)
    {
        return GetDirectorySnapshot(projectRoot, relativePath, topology).Entries
            .Select(static entry => entry.Clone())
            .ToList();
    }

    public async Task<string> ReadTextAsync(
        string projectRoot,
        string relativePath,
        Encoding? encoding = null,
        CancellationToken cancellationToken = default)
    {
        var fullPath = ResolveFullPath(projectRoot, relativePath);
        cancellationToken.ThrowIfCancellationRequested();
        encoding ??= Encoding.UTF8;
        return await File.ReadAllTextAsync(fullPath, encoding, cancellationToken);
    }

    public Task<byte[]> ReadBytesAsync(
        string projectRoot,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        var fullPath = ResolveFullPath(projectRoot, relativePath);
        cancellationToken.ThrowIfCancellationRequested();
        return File.ReadAllBytesAsync(fullPath, cancellationToken);
    }

    public async Task WriteTextAsync(
        string projectRoot,
        string relativePath,
        string content,
        Encoding? encoding = null,
        CancellationToken cancellationToken = default)
    {
        var fullPath = ResolveFullPath(projectRoot, relativePath);
        EnsureParentDirectory(fullPath);
        cancellationToken.ThrowIfCancellationRequested();
        encoding ??= Encoding.UTF8;
        await File.WriteAllTextAsync(fullPath, content, encoding, cancellationToken);
    }

    public async Task WriteBytesAsync(
        string projectRoot,
        string relativePath,
        byte[] content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var fullPath = ResolveFullPath(projectRoot, relativePath);
        EnsureParentDirectory(fullPath);
        cancellationToken.ThrowIfCancellationRequested();
        await File.WriteAllBytesAsync(fullPath, content, cancellationToken);
    }

    public WorkspaceDirectoryMetadataDocument? TryReadDirectoryMetadata(
        string projectRoot,
        string directoryRelativePath)
    {
        return WorkspaceMetadataFile.TryRead(projectRoot, directoryRelativePath);
    }

    public Task<WorkspaceDirectoryMetadataDocument> EnsureDirectoryMetadataAsync(
        string projectRoot,
        string directoryRelativePath,
        CancellationToken cancellationToken = default)
    {
        return WorkspaceMetadataFile.EnsureAsync(projectRoot, directoryRelativePath, cancellationToken);
    }

    public Task<WorkspaceDirectoryMetadataDocument> WriteDirectoryMetadataAsync(
        string projectRoot,
        string directoryRelativePath,
        WorkspaceDirectoryMetadataDocument document,
        CancellationToken cancellationToken = default)
    {
        return WorkspaceMetadataFile.WriteAsync(projectRoot, directoryRelativePath, document, cancellationToken);
    }

    public async Task<WorkspaceMetadataSyncResult> EnsureDirectoryMetadataTreeAsync(
        string projectRoot,
        WorkspaceTopologyContext topology,
        string relativePath = "",
        CancellationToken cancellationToken = default)
    {
        var normalizedRootPath = WorkspacePath.NormalizeRelativePath(relativePath);
        var startFullPath = ResolveFullPath(projectRoot, normalizedRootPath);
        if (!Directory.Exists(startFullPath))
            throw new DirectoryNotFoundException(WorkspaceConstants.Diagnostics.DirectoryDoesNotExistPrefix + normalizedRootPath);

        var excludes = DefaultExcludes.BuildWithCustom(topology.ExcludeDirs);
        var processedDirectoryCount = 0;
        var createdMetadataCount = 0;

        foreach (var directoryPath in EnumerateDirectoryTree(projectRoot, normalizedRootPath, excludes))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var existing = TryReadDirectoryMetadata(projectRoot, directoryPath);
            await EnsureDirectoryMetadataAsync(projectRoot, directoryPath, cancellationToken);

            processedDirectoryCount++;
            if (existing == null)
                createdMetadataCount++;
        }

        if (normalizedRootPath.Length == 0)
            InvalidateAll();
        else
            Invalidate(normalizedRootPath);

        return new WorkspaceMetadataSyncResult
        {
            RootRelativePath = normalizedRootPath,
            ProcessedDirectoryCount = processedDirectoryCount,
            CreatedMetadataCount = createdMetadataCount
        };
    }

    public void EnsureDirectory(string projectRoot, string relativePath)
    {
        var fullPath = ResolveFullPath(projectRoot, relativePath);
        Directory.CreateDirectory(fullPath);
    }

    public bool DeleteEntry(string projectRoot, string relativePath, bool recursive = false)
    {
        var fullPath = ResolveFullPath(projectRoot, relativePath);

        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
            return true;
        }

        if (Directory.Exists(fullPath))
        {
            Directory.Delete(fullPath, recursive);
            return true;
        }

        return false;
    }

    public void Invalidate(string relativePath)
    {
        treeCache.Invalidate(relativePath);
    }

    public void InvalidateAll()
    {
        treeCache.InvalidateAll();
    }

    public void Dispose()
    {
        treeCache.Dispose();
        GC.SuppressFinalize(this);
    }

    private static void EnsureParentDirectory(string fullPath)
    {
        var parent = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(parent))
            Directory.CreateDirectory(parent);
    }

    private static IEnumerable<string> EnumerateDirectoryTree(
        string projectRoot,
        string rootRelativePath,
        HashSet<string> excludes)
    {
        var stack = new Stack<string>();
        stack.Push(WorkspacePath.NormalizeRelativePath(rootRelativePath));

        while (stack.Count > 0)
        {
            var currentRelativePath = stack.Pop();
            var currentFullPath = WorkspacePath.ResolveFullPathWithinRoot(projectRoot, currentRelativePath);

            yield return currentRelativePath;

            IEnumerable<string> childDirectories;
            try
            {
                childDirectories = Directory.GetDirectories(currentFullPath)
                    .OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch
            {
                continue;
            }

            foreach (var childDirectory in childDirectories)
            {
                var name = Path.GetFileName(childDirectory);
                if (DefaultExcludes.IsExcludedDirectory(name, excludes))
                    continue;

                var childRelativePath = currentRelativePath.Length == 0
                    ? name
                    : $"{currentRelativePath}{WorkspaceConstants.Paths.RelativeSeparator}{name}";

                stack.Push(childRelativePath);
            }
        }
    }
}
