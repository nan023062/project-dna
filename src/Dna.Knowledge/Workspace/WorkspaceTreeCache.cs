using Dna.Knowledge.Workspace.Models;
using Microsoft.Extensions.Logging;

namespace Dna.Knowledge.Workspace;

/// <summary>
/// Directory snapshot cache with best-effort file system invalidation.
/// </summary>
public sealed class WorkspaceTreeCache : IDisposable
{
    private readonly ILogger _logger;
    private readonly object _lock = new();
    private readonly HashSet<string> _pendingInvalidations = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<WorkspaceChangeEntry> _pendingChanges = [];
    private readonly Dictionary<string, WorkspaceDirectorySnapshot> _cache = new(StringComparer.OrdinalIgnoreCase);

    private string _projectRoot = string.Empty;
    private HashSet<string> _excludes = DefaultExcludes.Dirs;
    private FileSystemWatcher? _watcher;
    private Timer? _debounceTimer;

    public WorkspaceTreeCache(ILogger logger)
    {
        _logger = logger;
    }

    public event EventHandler<WorkspaceChangeSet>? Changed;

    public void Initialize(string projectRoot, ArchitectureManifest architecture)
    {
        lock (_lock)
        {
            var normalizedRoot = Path.GetFullPath(projectRoot);
            var newExcludes = DefaultExcludes.BuildWithCustom(architecture.ExcludeDirs);

            if (string.Equals(_projectRoot, normalizedRoot, StringComparison.OrdinalIgnoreCase) &&
                _excludes.SetEquals(newExcludes))
            {
                return;
            }

            _projectRoot = normalizedRoot;
            _excludes = newExcludes;
            _cache.Clear();

            SetupWatcher();
            _logger.LogInformation(WorkspaceConstants.Diagnostics.CacheInitialized, normalizedRoot);
        }
    }

    public WorkspaceDirectorySnapshot GetRootSnapshot(
        Func<string, string, WorkspaceDirectorySnapshot> scanDirectoryFn)
    {
        return GetDirectorySnapshot(string.Empty, scanDirectoryFn);
    }

    public WorkspaceDirectorySnapshot GetDirectorySnapshot(
        string relativePath,
        Func<string, string, WorkspaceDirectorySnapshot> scanDirectoryFn)
    {
        var normalizedPath = WorkspacePath.NormalizeRelativePath(relativePath);

        lock (_lock)
        {
            if (_cache.TryGetValue(normalizedPath, out var cached))
                return cached.Clone();

            var snapshot = scanDirectoryFn(_projectRoot, normalizedPath);
            _cache[normalizedPath] = snapshot.Clone();
            return snapshot;
        }
    }

    public void Invalidate(string relativePath)
    {
        var normalizedPath = WorkspacePath.NormalizeRelativePath(relativePath);
        var isReset = false;

        lock (_lock)
        {
            if (normalizedPath.Length == 0)
            {
                _cache.Clear();
                isReset = true;
            }
            else
            {
                var keysToRemove = _cache.Keys
                    .Where(key =>
                        string.Equals(key, normalizedPath, StringComparison.OrdinalIgnoreCase) ||
                        key.StartsWith(normalizedPath + "/", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(key, WorkspacePath.GetParentPath(normalizedPath), StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var key in keysToRemove)
                    _cache.Remove(key);
            }
        }

        PublishChanges(new WorkspaceChangeSet
        {
            ProjectRoot = _projectRoot,
            OccurredAtUtc = DateTime.UtcNow,
            Entries =
            [
                new WorkspaceChangeEntry
                {
                    Kind = isReset ? WorkspaceChangeKind.Reset : WorkspaceChangeKind.Invalidated,
                    TargetKind = isReset ? WorkspaceChangeTargetKind.Directory : WorkspaceChangeTargetKind.Unknown,
                    Path = isReset ? string.Empty : normalizedPath,
                    ParentPath = isReset ? string.Empty : WorkspacePath.GetParentPath(normalizedPath)
                }
            ]
        });
    }

    public void InvalidateAll()
    {
        lock (_lock)
            _cache.Clear();

        PublishChanges(new WorkspaceChangeSet
        {
            ProjectRoot = _projectRoot,
            OccurredAtUtc = DateTime.UtcNow,
            Entries =
            [
                new WorkspaceChangeEntry
                {
                    Kind = WorkspaceChangeKind.Reset,
                    TargetKind = WorkspaceChangeTargetKind.Directory,
                    Path = string.Empty,
                    ParentPath = string.Empty
                }
            ]
        });
    }

    private void SetupWatcher()
    {
        _watcher?.Dispose();
        _watcher = null;

        if (_projectRoot.Length == 0 || !Directory.Exists(_projectRoot))
            return;

        try
        {
            _watcher = new FileSystemWatcher(_projectRoot)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };

            _watcher.Created += OnFileSystemChanged;
            _watcher.Changed += OnFileSystemChanged;
            _watcher.Deleted += OnFileSystemChanged;
            _watcher.Renamed += OnFileSystemRenamed;
            _watcher.Error += OnWatcherError;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, WorkspaceConstants.Diagnostics.WatcherSetupFailed);
            _watcher = null;
        }
    }

    private void OnFileSystemChanged(object sender, FileSystemEventArgs e)
    {
        if (ShouldIgnore(e.FullPath))
            return;

        ScheduleInvalidation(
            e.FullPath,
            MapChangeKind(e.ChangeType),
            ResolveTargetKind(e.FullPath));
    }

    private void OnFileSystemRenamed(object sender, RenamedEventArgs e)
    {
        if (!ShouldIgnore(e.OldFullPath))
            ScheduleInvalidation(
                e.OldFullPath,
                WorkspaceChangeKind.Renamed,
                ResolveTargetKind(e.OldFullPath),
                e.FullPath);

        if (!ShouldIgnore(e.FullPath))
            ScheduleInvalidation(
                e.FullPath,
                WorkspaceChangeKind.Renamed,
                ResolveTargetKind(e.FullPath),
                e.OldFullPath);
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        _logger.LogWarning(e.GetException(), WorkspaceConstants.Diagnostics.WatcherError);
        InvalidateAll();
        SetupWatcher();
    }

    private bool ShouldIgnore(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
            return true;

        string relativePath;
        try
        {
            relativePath = Path.GetRelativePath(_projectRoot, fullPath);
        }
        catch
        {
            return true;
        }

        foreach (var segment in WorkspacePath.EnumerateSegments(relativePath))
        {
            if (_excludes.Contains(segment))
                return true;
        }

        return false;
    }

    private void ScheduleInvalidation(
        string fullPath,
        WorkspaceChangeKind kind,
        WorkspaceChangeTargetKind targetKind,
        string? otherPath = null)
    {
        var relativePath = GetRelativePath(fullPath);
        var parentPath = WorkspacePath.GetParentPath(relativePath);

        lock (_pendingInvalidations)
        {
            if (relativePath.Length > 0)
                _pendingInvalidations.Add(relativePath);
            _pendingInvalidations.Add(parentPath);
        }

        lock (_pendingChanges)
        {
            _pendingChanges.Add(new WorkspaceChangeEntry
            {
                Kind = kind,
                TargetKind = targetKind,
                Path = relativePath,
                ParentPath = parentPath,
                PreviousPath = string.IsNullOrWhiteSpace(otherPath) ? null : GetRelativePath(otherPath)
            });
        }

        _debounceTimer ??= new Timer(_ => FlushInvalidations());
        _debounceTimer.Change(WorkspaceConstants.Timing.WatcherDebounceMilliseconds, Timeout.Infinite);
    }

    private void FlushInvalidations()
    {
        List<string> paths;
        List<WorkspaceChangeEntry> changes;
        lock (_pendingInvalidations)
        {
            paths = [.. _pendingInvalidations];
            _pendingInvalidations.Clear();
        }

        lock (_pendingChanges)
        {
            changes = [.. _pendingChanges];
            _pendingChanges.Clear();
        }

        foreach (var path in paths)
            InvalidateCore(path);

        if (changes.Count > 0)
        {
            PublishChanges(new WorkspaceChangeSet
            {
                ProjectRoot = _projectRoot,
                OccurredAtUtc = DateTime.UtcNow,
                Entries = DeduplicateChanges(changes)
            });
        }
    }

    private void InvalidateCore(string relativePath)
    {
        var normalizedPath = WorkspacePath.NormalizeRelativePath(relativePath);

        lock (_lock)
        {
            if (normalizedPath.Length == 0)
            {
                _cache.Clear();
                return;
            }

            var keysToRemove = _cache.Keys
                .Where(key =>
                    string.Equals(key, normalizedPath, StringComparison.OrdinalIgnoreCase) ||
                    key.StartsWith(normalizedPath + "/", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(key, WorkspacePath.GetParentPath(normalizedPath), StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var key in keysToRemove)
                _cache.Remove(key);
        }
    }

    private string GetRelativePath(string fullPath)
    {
        if (_projectRoot.Length == 0)
            return WorkspacePath.NormalizeRelativePath(fullPath);

        if (!fullPath.StartsWith(_projectRoot, StringComparison.OrdinalIgnoreCase))
            return WorkspacePath.NormalizeRelativePath(fullPath);

        var relative = fullPath[_projectRoot.Length..]
            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return WorkspacePath.NormalizeRelativePath(relative);
    }

    private static WorkspaceChangeKind MapChangeKind(WatcherChangeTypes changeType)
    {
        return changeType switch
        {
            WatcherChangeTypes.Created => WorkspaceChangeKind.Created,
            WatcherChangeTypes.Changed => WorkspaceChangeKind.Changed,
            WatcherChangeTypes.Deleted => WorkspaceChangeKind.Deleted,
            WatcherChangeTypes.Renamed => WorkspaceChangeKind.Renamed,
            _ => WorkspaceChangeKind.Changed
        };
    }

    private static WorkspaceChangeTargetKind ResolveTargetKind(string fullPath)
    {
        if (Directory.Exists(fullPath))
            return WorkspaceChangeTargetKind.Directory;
        if (File.Exists(fullPath))
            return WorkspaceChangeTargetKind.File;
        return WorkspaceChangeTargetKind.Unknown;
    }

    private static List<WorkspaceChangeEntry> DeduplicateChanges(List<WorkspaceChangeEntry> changes)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var deduped = new List<WorkspaceChangeEntry>();

        foreach (var change in changes)
        {
            var key = $"{change.Kind}|{change.TargetKind}|{change.Path}|{change.PreviousPath}";
            if (!seen.Add(key))
                continue;

            deduped.Add(change);
        }

        return deduped;
    }

    private void PublishChanges(WorkspaceChangeSet changeSet)
    {
        Changed?.Invoke(this, changeSet);
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _watcher = null;

        _debounceTimer?.Dispose();
        _debounceTimer = null;

        GC.SuppressFinalize(this);
    }
}
