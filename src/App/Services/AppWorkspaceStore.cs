using System.Text.Json;

namespace Dna.App.Services;

public sealed class AppWorkspaceStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly object _gate = new();
    private readonly string _configPath;
    private readonly string _defaultServerBaseUrl;
    private readonly string _defaultWorkspaceRoot;
    private WorkspaceConfigState _state;

    public AppWorkspaceStore(AppRuntimeOptions options)
    {
        _defaultServerBaseUrl = AppBootstrap.NormalizeUrl(options.ApiBaseUrl);
        _defaultWorkspaceRoot = Path.GetFullPath(options.WorkspaceRoot);
        _configPath = string.IsNullOrWhiteSpace(options.WorkspaceConfigPath)
            ? ResolveDefaultConfigPath(options)
            : Path.GetFullPath(options.WorkspaceConfigPath);

        _state = LoadState();
        EnsureInitialized();
    }

    public AppWorkspaceSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            var current = ResolveCurrentWorkspace();
            return new AppWorkspaceSnapshot
            {
                CurrentWorkspaceId = current.Id,
                CurrentWorkspace = Clone(current),
                Workspaces = _state.Workspaces.Select(Clone).ToList(),
                Defaults = new AppWorkspaceDefaults
                {
                    ServerBaseUrl = _defaultServerBaseUrl,
                    WorkspaceRoot = _defaultWorkspaceRoot,
                    Mode = InferWorkspaceMode(_defaultServerBaseUrl)
                }
            };
        }
    }

    public AppWorkspaceRecord GetCurrentWorkspace()
    {
        lock (_gate)
        {
            return Clone(ResolveCurrentWorkspace());
        }
    }

    public AppWorkspaceRecord CreateWorkspace(AppWorkspaceUpsertRequest request)
    {
        lock (_gate)
        {
            var workspace = BuildWorkspace(request, workspaceId: CreateWorkspaceId());
            _state.Workspaces.Add(workspace);

            if (request.SetCurrent == true || string.IsNullOrWhiteSpace(_state.CurrentWorkspaceId))
                _state.CurrentWorkspaceId = workspace.Id;

            SaveState();
            return Clone(workspace);
        }
    }

    public AppWorkspaceRecord UpdateWorkspace(string workspaceId, AppWorkspaceUpsertRequest request)
    {
        lock (_gate)
        {
            var existing = _state.Workspaces.FirstOrDefault(item => item.Id == workspaceId)
                ?? throw new KeyNotFoundException($"Workspace not found: {workspaceId}");

            existing.Name = NormalizeName(request.Name);
            existing.Mode = NormalizeMode(request.Mode);
            existing.ServerBaseUrl = NormalizeServerBaseUrl(request.ServerBaseUrl);
            existing.WorkspaceRoot = NormalizeWorkspaceRoot(request.WorkspaceRoot);
            existing.UpdatedAtUtc = DateTime.UtcNow;

            if (request.SetCurrent == true)
                _state.CurrentWorkspaceId = existing.Id;

            SaveState();
            return Clone(existing);
        }
    }

    public AppWorkspaceRecord SetCurrentWorkspace(string workspaceId)
    {
        lock (_gate)
        {
            var workspace = _state.Workspaces.FirstOrDefault(item => item.Id == workspaceId)
                ?? throw new KeyNotFoundException($"Workspace not found: {workspaceId}");

            _state.CurrentWorkspaceId = workspace.Id;
            workspace.UpdatedAtUtc = DateTime.UtcNow;
            SaveState();
            return Clone(workspace);
        }
    }

    public AppWorkspaceRecord SetCurrentServer(string serverBaseUrl, string? displayName = null)
    {
        lock (_gate)
        {
            var current = ResolveCurrentWorkspace();
            current.ServerBaseUrl = NormalizeServerBaseUrl(serverBaseUrl);
            current.Mode = InferWorkspaceMode(current.ServerBaseUrl);
            if (!string.IsNullOrWhiteSpace(displayName))
                current.Name = displayName.Trim();
            current.UpdatedAtUtc = DateTime.UtcNow;
            SaveState();
            return Clone(current);
        }
    }

    public int SyncDiscoveredServers(IEnumerable<DiscoveredServerInfo> discoveredServers)
    {
        lock (_gate)
        {
            var changed = 0;
            foreach (var discovered in discoveredServers.Where(item => item.Allowed))
            {
                var normalizedUrl = NormalizeServerBaseUrl(discovered.BaseUrl);
                var normalizedName = BuildDiscoveredWorkspaceName(discovered, normalizedUrl);

                var existing = _state.Workspaces.FirstOrDefault(item =>
                    string.Equals(item.ServerBaseUrl, normalizedUrl, StringComparison.OrdinalIgnoreCase));
                if (existing is not null)
                {
                    var updated = false;
                    if (!string.Equals(existing.Name, normalizedName, StringComparison.Ordinal))
                    {
                        existing.Name = normalizedName;
                        updated = true;
                    }

                    var inferredMode = InferWorkspaceMode(normalizedUrl);
                    if (!string.Equals(existing.Mode, inferredMode, StringComparison.OrdinalIgnoreCase))
                    {
                        existing.Mode = inferredMode;
                        updated = true;
                    }

                    if (updated)
                    {
                        existing.UpdatedAtUtc = DateTime.UtcNow;
                        changed++;
                    }

                    continue;
                }

                _state.Workspaces.Add(new AppWorkspaceRecord
                {
                    Id = CreateWorkspaceId(),
                    Name = normalizedName,
                    Mode = InferWorkspaceMode(normalizedUrl),
                    ServerBaseUrl = normalizedUrl,
                    WorkspaceRoot = _defaultWorkspaceRoot,
                    UpdatedAtUtc = DateTime.UtcNow
                });
                changed++;
            }

            if (changed > 0)
                SaveState();

            return changed;
        }
    }

    public AppWorkspaceRecord DeleteWorkspace(string workspaceId)
    {
        lock (_gate)
        {
            var index = _state.Workspaces.FindIndex(item => item.Id == workspaceId);
            if (index < 0)
                throw new KeyNotFoundException($"Workspace not found: {workspaceId}");

            var removed = Clone(_state.Workspaces[index]);
            _state.Workspaces.RemoveAt(index);

            if (_state.Workspaces.Count == 0)
            {
                var fallback = CreateDefaultWorkspace();
                _state.Workspaces.Add(fallback);
                _state.CurrentWorkspaceId = fallback.Id;
            }
            else if (string.Equals(_state.CurrentWorkspaceId, workspaceId, StringComparison.Ordinal))
            {
                _state.CurrentWorkspaceId = _state.Workspaces[0].Id;
            }

            SaveState();
            return removed;
        }
    }

    private WorkspaceConfigState LoadState()
    {
        try
        {
            if (!File.Exists(_configPath))
                return new WorkspaceConfigState();

            var json = File.ReadAllText(_configPath);
            return JsonSerializer.Deserialize<WorkspaceConfigState>(json, JsonOptions) ?? new WorkspaceConfigState();
        }
        catch
        {
            return new WorkspaceConfigState();
        }
    }

    private void SaveState()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
        var json = JsonSerializer.Serialize(_state, JsonOptions);
        File.WriteAllText(_configPath, json);
    }

    private void EnsureInitialized()
    {
        if (_state.Workspaces.Count == 0)
            _state.Workspaces.Add(CreateDefaultWorkspace());
        else
            SyncDefaultWorkspace();

        if (_state.Workspaces.All(item => item.Id != _state.CurrentWorkspaceId))
            _state.CurrentWorkspaceId = _state.Workspaces[0].Id;

        SaveState();
    }

    private AppWorkspaceRecord ResolveCurrentWorkspace()
    {
        return _state.Workspaces.FirstOrDefault(item => item.Id == _state.CurrentWorkspaceId)
               ?? _state.Workspaces[0];
    }

    private AppWorkspaceRecord CreateDefaultWorkspace()
    {
        return new AppWorkspaceRecord
        {
            Id = "default",
            Name = IsLocalServer(_defaultServerBaseUrl) ? "默认本地工作区" : "默认团队工作区",
            Mode = InferWorkspaceMode(_defaultServerBaseUrl),
            ServerBaseUrl = _defaultServerBaseUrl,
            WorkspaceRoot = _defaultWorkspaceRoot,
            UpdatedAtUtc = DateTime.UtcNow
        };
    }

    private void SyncDefaultWorkspace()
    {
        var existingDefault = _state.Workspaces.FirstOrDefault(item => item.Id == "default");
        if (existingDefault is null)
        {
            _state.Workspaces.Insert(0, CreateDefaultWorkspace());
            return;
        }

        var defaults = CreateDefaultWorkspace();
        existingDefault.Name = defaults.Name;
        existingDefault.Mode = defaults.Mode;
        existingDefault.ServerBaseUrl = defaults.ServerBaseUrl;
        existingDefault.WorkspaceRoot = defaults.WorkspaceRoot;
        existingDefault.UpdatedAtUtc = DateTime.UtcNow;
    }

    private AppWorkspaceRecord BuildWorkspace(AppWorkspaceUpsertRequest request, string workspaceId)
    {
        return new AppWorkspaceRecord
        {
            Id = workspaceId,
            Name = NormalizeName(request.Name),
            Mode = NormalizeMode(request.Mode),
            ServerBaseUrl = NormalizeServerBaseUrl(request.ServerBaseUrl),
            WorkspaceRoot = NormalizeWorkspaceRoot(request.WorkspaceRoot),
            UpdatedAtUtc = DateTime.UtcNow
        };
    }

    private static AppWorkspaceRecord Clone(AppWorkspaceRecord source)
    {
        return new AppWorkspaceRecord
        {
            Id = source.Id,
            Name = source.Name,
            Mode = source.Mode,
            ServerBaseUrl = source.ServerBaseUrl,
            WorkspaceRoot = source.WorkspaceRoot,
            UpdatedAtUtc = source.UpdatedAtUtc
        };
    }

    private string NormalizeName(string? raw)
    {
        var normalized = raw?.Trim();
        if (!string.IsNullOrWhiteSpace(normalized))
            return normalized;

        return IsLocalServer(_defaultServerBaseUrl) ? "未命名工作区" : "未命名团队工作区";
    }

    private static string NormalizeMode(string? raw)
    {
        var normalized = (raw ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "personal" => "personal",
            "team" => "team",
            _ => throw new ArgumentException("mode must be either 'personal' or 'team'.")
        };
    }

    private static string InferWorkspaceMode(string serverBaseUrl)
        => IsLocalServer(serverBaseUrl) ? "personal" : "team";

    private static bool IsLocalServer(string serverBaseUrl)
    {
        if (!Uri.TryCreate(serverBaseUrl, UriKind.Absolute, out var uri))
            return false;

        return uri.IsLoopback ||
               string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeServerBaseUrl(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new ArgumentException("serverBaseUrl is required.");

        var normalized = AppBootstrap.NormalizeUrl(raw);
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
            throw new ArgumentException($"serverBaseUrl is not a valid absolute URL: {raw}");

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("serverBaseUrl must use http or https.");
        }

        return uri.GetLeftPart(UriPartial.Authority);
    }

    private static string NormalizeWorkspaceRoot(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new ArgumentException("workspaceRoot is required.");

        var fullPath = Path.GetFullPath(raw);
        if (!Directory.Exists(fullPath))
            throw new ArgumentException($"workspaceRoot does not exist: {fullPath}");

        return fullPath;
    }

    private static string CreateWorkspaceId()
        => $"ws-{Guid.NewGuid():N}"[..11];

    private static string BuildDiscoveredWorkspaceName(DiscoveredServerInfo discovered, string normalizedUrl)
    {
        if (Uri.TryCreate(normalizedUrl, UriKind.Absolute, out var uri))
            return uri.Host;

        return normalizedUrl;
    }

    private static string ResolveDefaultConfigPath(AppRuntimeOptions options)
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".agentic-os",
            "app-workspaces.json");
    }

    private sealed class WorkspaceConfigState
    {
        public string? CurrentWorkspaceId { get; set; }
        public List<AppWorkspaceRecord> Workspaces { get; set; } = [];
    }
}

public sealed class AppWorkspaceSnapshot
{
    public string CurrentWorkspaceId { get; set; } = string.Empty;
    public AppWorkspaceRecord CurrentWorkspace { get; set; } = new();
    public List<AppWorkspaceRecord> Workspaces { get; set; } = [];
    public AppWorkspaceDefaults Defaults { get; set; } = new();
}

public sealed class AppWorkspaceDefaults
{
    public string ServerBaseUrl { get; set; } = string.Empty;
    public string WorkspaceRoot { get; set; } = string.Empty;
    public string Mode { get; set; } = "personal";
}

public sealed class AppWorkspaceRecord
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Mode { get; set; } = "personal";
    public string ServerBaseUrl { get; set; } = string.Empty;
    public string WorkspaceRoot { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class AppWorkspaceUpsertRequest
{
    public string? Name { get; init; }
    public string? Mode { get; init; }
    public string? ServerBaseUrl { get; init; }
    public string? WorkspaceRoot { get; init; }
    public bool? SetCurrent { get; init; }
}
