using System.Text.Json;

namespace Dna.Client.Desktop;

public sealed class DesktopRecentProjectsStore
{
    private const int MaxRecentProjects = 20;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _storePath;

    public DesktopRecentProjectsStore(string storePath)
    {
        _storePath = storePath;
    }

    public static DesktopRecentProjectsStore CreateDefault()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".dna",
            "client-desktop",
            "recent-projects.json");

        return new DesktopRecentProjectsStore(path);
    }

    public IReadOnlyList<DesktopRecentProjectEntry> Load()
    {
        var state = TryReadState();
        var filtered = NormalizeAndFilter(state.Projects);

        if (filtered.Count != state.Projects.Count)
            Save(filtered);

        return filtered;
    }

    public IReadOnlyList<DesktopRecentProjectEntry> Upsert(DesktopProjectConfig project)
    {
        var current = Load().ToList();
        current.RemoveAll(entry => SameProjectRoot(entry.ProjectRoot, project.ProjectRoot));

        current.Insert(0, new DesktopRecentProjectEntry
        {
            ProjectRoot = project.ProjectRoot,
            ProjectName = project.ProjectName,
            ServerBaseUrl = project.ServerBaseUrl,
            ConfigPath = project.ConfigPath,
            LastOpenedAtUtc = DateTime.UtcNow
        });

        var normalized = NormalizeAndFilter(current);
        Save(normalized);
        return normalized;
    }

    public IReadOnlyList<DesktopRecentProjectEntry> Remove(string projectRoot)
    {
        var current = Load().ToList();
        current.RemoveAll(entry => SameProjectRoot(entry.ProjectRoot, projectRoot));

        Save(current);
        return current;
    }

    private RecentProjectsState TryReadState()
    {
        if (!File.Exists(_storePath))
            return new RecentProjectsState();

        try
        {
            var json = File.ReadAllText(_storePath);
            var state = JsonSerializer.Deserialize<RecentProjectsState>(json, JsonOptions);
            return state ?? new RecentProjectsState();
        }
        catch
        {
            return new RecentProjectsState();
        }
    }

    private void Save(IReadOnlyList<DesktopRecentProjectEntry> projects)
    {
        var dir = Path.GetDirectoryName(_storePath)
                  ?? throw new InvalidOperationException("无法确定最近项目存储目录。");

        Directory.CreateDirectory(dir);

        var state = new RecentProjectsState
        {
            Projects = projects.ToList()
        };

        var json = JsonSerializer.Serialize(state, JsonOptions);
        File.WriteAllText(_storePath, json);
    }

    private static List<DesktopRecentProjectEntry> NormalizeAndFilter(IEnumerable<DesktopRecentProjectEntry> source)
    {
        var result = new List<DesktopRecentProjectEntry>();
        var seenRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in source
                     .OrderByDescending(x => x.LastOpenedAtUtc))
        {
            var root = (item.ProjectRoot ?? string.Empty).Trim();
            var projectName = (item.ProjectName ?? string.Empty).Trim();
            var serverBaseUrl = (item.ServerBaseUrl ?? string.Empty).Trim();
            var configPath = (item.ConfigPath ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(root) ||
                string.IsNullOrWhiteSpace(projectName) ||
                !Directory.Exists(root))
            {
                continue;
            }

            var normalizedRoot = Path.GetFullPath(root);
            if (!seenRoots.Add(normalizedRoot))
                continue;

            result.Add(new DesktopRecentProjectEntry
            {
                ProjectRoot = normalizedRoot,
                ProjectName = projectName,
                ServerBaseUrl = serverBaseUrl,
                ConfigPath = configPath,
                LastOpenedAtUtc = item.LastOpenedAtUtc == default ? DateTime.UtcNow : item.LastOpenedAtUtc
            });

            if (result.Count >= MaxRecentProjects)
                break;
        }

        return result;
    }

    private static bool SameProjectRoot(string left, string right)
    {
        return string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            StringComparison.OrdinalIgnoreCase);
    }

    private sealed class RecentProjectsState
    {
        public List<DesktopRecentProjectEntry> Projects { get; init; } = [];
    }
}

public sealed class DesktopRecentProjectEntry
{
    public required string ProjectRoot { get; init; }
    public required string ProjectName { get; init; }
    public required string ServerBaseUrl { get; init; }
    public required string ConfigPath { get; init; }
    public DateTime LastOpenedAtUtc { get; init; }
}
