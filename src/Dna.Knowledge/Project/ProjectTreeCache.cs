using Dna.Knowledge.Models;
using Dna.Knowledge.Project.Models;
using Microsoft.Extensions.Logging;

namespace Dna.Knowledge.Project;

/// <summary>
/// 工程文件树缓存 — 缓存目录结构 + FileSystemWatcher 增量更新。
/// 从项目根目录开始，按需单层扫描，避免每次 API 请求都遍历磁盘。
/// </summary>
internal class ProjectTreeCache : IDisposable
{
    private readonly ILogger _logger;
    private readonly object _lock = new();

    private string _projectRoot = string.Empty;
    private HashSet<string> _excludes = DefaultExcludes.Dirs;

    private readonly Dictionary<string, List<ProjectFileNode>> _cache = new(StringComparer.OrdinalIgnoreCase);

    private FileSystemWatcher? _watcher;
    private Timer? _debounceTimer;
    private readonly HashSet<string> _pendingInvalidations = new(StringComparer.OrdinalIgnoreCase);

    public ProjectTreeCache(ILogger logger)
    {
        _logger = logger;
    }

    public void Initialize(string projectRoot, ArchitectureManifest architecture)
    {
        lock (_lock)
        {
            var newExcludes = DefaultExcludes.BuildWithCustom(architecture.ExcludeDirs);

            if (_projectRoot == projectRoot && _excludes.SetEquals(newExcludes))
                return;

            _projectRoot = projectRoot;
            _excludes = newExcludes;
            _cache.Clear();

            SetupWatcher();
            _logger.LogInformation("ProjectTreeCache 已初始化: {Root}", projectRoot);
        }
    }

    /// <summary>获取项目根目录的子目录（首次加载）</summary>
    public List<ProjectFileNode> GetRoots(
        Func<string, string, List<ProjectFileNode>> scanChildrenFn)
    {
        return GetChildren("", scanChildrenFn);
    }

    /// <summary>获取指定目录的子级（带缓存）</summary>
    public List<ProjectFileNode> GetChildren(
        string relativePath,
        Func<string, string, List<ProjectFileNode>> scanChildrenFn)
    {
        var key = NormalizePath(relativePath);
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var cached))
                return cached;

            var children = scanChildrenFn(_projectRoot, relativePath);
            _cache[key] = children;
            return children;
        }
    }

    public HashSet<string> GetExcludes()
    {
        lock (_lock) return _excludes;
    }

    /// <summary>使指定目录的缓存失效</summary>
    public void Invalidate(string relativePath)
    {
        lock (_lock)
        {
            var normalized = NormalizePath(relativePath);

            _cache.Remove(normalized);

            var parentPath = normalized.Contains('/')
                ? normalized[..normalized.LastIndexOf('/')]
                : "";
            _cache.Remove(parentPath);
        }
    }

    /// <summary>清除所有缓存</summary>
    public void InvalidateAll()
    {
        lock (_lock) _cache.Clear();
    }

    // ═══════════════════════════════════════════
    //  FileSystemWatcher
    // ═══════════════════════════════════════════

    private void SetupWatcher()
    {
        _watcher?.Dispose();
        _watcher = null;

        if (string.IsNullOrEmpty(_projectRoot) || !Directory.Exists(_projectRoot))
            return;

        try
        {
            _watcher = new FileSystemWatcher(_projectRoot)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.DirectoryName,
                EnableRaisingEvents = true
            };

            _watcher.Created += OnDirectoryChanged;
            _watcher.Deleted += OnDirectoryChanged;
            _watcher.Renamed += OnDirectoryRenamed;
            _watcher.Error += OnWatcherError;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FileSystemWatcher 创建失败");
            _watcher = null;
        }
    }

    private void OnDirectoryChanged(object sender, FileSystemEventArgs e)
    {
        var dirName = Path.GetFileName(e.FullPath);
        if (_excludes.Contains(dirName)) return;
        ScheduleInvalidation(GetRelativePath(e.FullPath));
    }

    private void OnDirectoryRenamed(object sender, RenamedEventArgs e)
    {
        ScheduleInvalidation(GetRelativePath(e.OldFullPath));
        ScheduleInvalidation(GetRelativePath(e.FullPath));
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        _logger.LogWarning(e.GetException(), "FileSystemWatcher 错误，重建");
        InvalidateAll();
        SetupWatcher();
    }

    private void ScheduleInvalidation(string relativePath)
    {
        lock (_pendingInvalidations)
        {
            _pendingInvalidations.Add(relativePath);
        }
        _debounceTimer ??= new Timer(_ => FlushInvalidations());
        _debounceTimer.Change(300, Timeout.Infinite);
    }

    private void FlushInvalidations()
    {
        List<string> paths;
        lock (_pendingInvalidations)
        {
            paths = [.. _pendingInvalidations];
            _pendingInvalidations.Clear();
        }

        foreach (var path in paths)
            Invalidate(path);
    }

    private string GetRelativePath(string fullPath)
    {
        if (fullPath.StartsWith(_projectRoot, StringComparison.OrdinalIgnoreCase))
        {
            var rel = fullPath[_projectRoot.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return NormalizePath(rel);
        }
        return NormalizePath(fullPath);
    }

    private static string NormalizePath(string path) =>
        path.Replace('\\', '/').Trim('/');

    public void Dispose()
    {
        _watcher?.Dispose();
        _watcher = null;
        _debounceTimer?.Dispose();
        _debounceTimer = null;
        GC.SuppressFinalize(this);
    }
}
