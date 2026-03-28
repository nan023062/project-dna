using System.Text.Json;

namespace Dna.Core.Config;

/// <summary>
/// Project DNA 全局配置：项目路径管理 + 最近项目记忆
/// 支持运行时切换项目、持久化最近打开的项目列表
/// </summary>
public class ProjectConfig
{
    private static readonly string ConfigDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dna");

    private static readonly string ConfigFile = Path.Combine(ConfigDir, "config.json");

    private string _projectRoot;

    public string DefaultProjectRoot => _projectRoot;

    public ProjectConfig()
    {
        _projectRoot = Environment.GetEnvironmentVariable("DNA_PROJECT_ROOT")
                       ?? Environment.GetEnvironmentVariable("DNA_MCP_PROJECT_ROOT")
                       ?? string.Empty;

        if (string.IsNullOrEmpty(_projectRoot))
        {
            var saved = LoadPersistedConfig();
            if (!string.IsNullOrEmpty(saved.LastProject) && Directory.Exists(saved.LastProject))
                _projectRoot = saved.LastProject;
        }
    }

    public string Resolve(string? provided)
    {
        if (!string.IsNullOrWhiteSpace(provided))
            return NormalizeProjectRootCandidate(provided);

        if (!string.IsNullOrWhiteSpace(_projectRoot))
            return NormalizeProjectRootCandidate(_projectRoot);

        throw new InvalidOperationException(
            "未指定 projectRoot。请通过 Dashboard 选择项目，或设置环境变量 DNA_PROJECT_ROOT。");
    }

    public bool HasProject => !string.IsNullOrEmpty(_projectRoot);

    /// <summary>
    /// 任务级节流间隔（毫秒）。用于 Agent 每次工具调用后主动等待，降低 LLM 429 风险。
    /// 优先级：环境变量 DNA_TOOL_THROTTLE_MS > config.json 配置 > 默认值 300ms
    /// </summary>
    public int GetAgentToolThrottleMs()
    {
        var env = Environment.GetEnvironmentVariable("DNA_TOOL_THROTTLE_MS");
        if (int.TryParse(env, out var envMs))
            return Math.Clamp(envMs, 0, 2000);

        var config = LoadPersistedConfig();
        return Math.Clamp(config.AgentToolThrottleMs ?? 300, 0, 2000);
    }

    /// <summary>
    /// Agent 工程治理模式。
    /// - cursor-like: 交付优先，默认宽松（仅硬边界阻断）
    /// - governance-first: 治理优先，启用严格闸门
    /// 优先级：环境变量 DNA_GOVERNANCE_MODE > config.json > 默认 cursor-like
    /// </summary>
    public string GetAgentGovernanceMode()
    {
        var env = Environment.GetEnvironmentVariable("DNA_GOVERNANCE_MODE");
        if (!string.IsNullOrWhiteSpace(env))
            return NormalizeGovernanceMode(env);

        var config = LoadPersistedConfig();
        return NormalizeGovernanceMode(config.AgentGovernanceMode);
    }

    public bool IsGovernanceFirstMode()
        => GetAgentGovernanceMode() == "governance-first";

    private static string NormalizeGovernanceMode(string? raw)
    {
        var normalized = (raw ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "governance-first" => "governance-first",
            "strict" => "governance-first",
            _ => "cursor-like"
        };
    }

    /// <summary>
    /// 运行时切换项目根目录，同时持久化到配置文件
    /// </summary>
    public SetProjectResult SetProject(string projectRoot)
    {
        var requested = Path.GetFullPath(projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var normalized = NormalizeProjectRootCandidate(requested);

        if (!Directory.Exists(normalized))
            return new SetProjectResult(false, $"目录不存在：{normalized}");

        _projectRoot = normalized;
        Environment.SetEnvironmentVariable("DNA_PROJECT_ROOT", normalized);

        AddToRecent(normalized);
        if (!string.Equals(requested, normalized, StringComparison.OrdinalIgnoreCase))
        {
            var nestedDna = Path.Combine(requested, ".dna");
            if (Directory.Exists(nestedDna))
            {
                return new SetProjectResult(
                    true,
                    $"检测到你选择的是子目录，已自动修正项目根目录：{normalized}。同时发现子目录下存在误创建的 .dna：{nestedDna}（确认无用后可删除）。");
            }
            return new SetProjectResult(true, $"检测到你选择的是子目录，已自动修正项目根目录：{normalized}");
        }
        return new SetProjectResult(true, $"项目已切换到：{normalized}");
    }

    /// <summary>
    /// 获取最近打开的项目列表（最多 10 个，最新在前）
    /// </summary>
    public List<RecentProject> GetRecentProjects()
    {
        var config = LoadPersistedConfig();
        return config.RecentProjects
            .Where(p => Directory.Exists(p.Path))
            .Take(10)
            .ToList();
    }

    // ── LLM Provider 管理 ──

    public List<LlmProviderConfig> GetLlmProviders()
        => LoadPersistedConfig().LlmProviders;

    public LlmProviderConfig? GetActiveLlmProvider()
    {
        var config = LoadPersistedConfig();
        if (string.IsNullOrEmpty(config.ActiveLlmProviderId))
            return config.LlmProviders.FirstOrDefault();
        return config.LlmProviders.FirstOrDefault(p => p.Id == config.ActiveLlmProviderId)
               ?? config.LlmProviders.FirstOrDefault();
    }

    public void SetActiveLlmProvider(string providerId)
    {
        var config = LoadPersistedConfig();
        config.ActiveLlmProviderId = providerId;
        SavePersistedConfig(config);
    }

    public LlmProviderConfig SaveLlmProvider(LlmProviderConfig provider)
    {
        provider.ApiKey = provider.ApiKey?.Trim() ?? "";
        provider.BaseUrl = provider.BaseUrl?.Trim() ?? "";
        provider.Model = provider.Model?.Trim() ?? "";
        provider.EmbeddingBaseUrl = provider.EmbeddingBaseUrl?.Trim() ?? "";
        provider.EmbeddingModel = provider.EmbeddingModel?.Trim() ?? "";

        var config = LoadPersistedConfig();
        var existing = config.LlmProviders.FindIndex(p => p.Id == provider.Id);
        if (existing >= 0)
        {
            if (string.IsNullOrEmpty(provider.ApiKey))
                provider.ApiKey = config.LlmProviders[existing].ApiKey;
            config.LlmProviders[existing] = provider;
        }
        else
        {
            if (string.IsNullOrEmpty(provider.Id))
                provider.Id = Guid.NewGuid().ToString("N")[..8];
            config.LlmProviders.Add(provider);
        }
        if (config.LlmProviders.Count == 1)
            config.ActiveLlmProviderId = provider.Id;
        SavePersistedConfig(config);
        return provider;
    }

    public bool DeleteLlmProvider(string providerId)
    {
        var config = LoadPersistedConfig();
        var removed = config.LlmProviders.RemoveAll(p => p.Id == providerId);
        if (removed > 0)
        {
            if (config.ActiveLlmProviderId == providerId)
                config.ActiveLlmProviderId = config.LlmProviders.FirstOrDefault()?.Id;
            SavePersistedConfig(config);
        }
        return removed > 0;
    }

    private void AddToRecent(string projectRoot)
    {
        var config = LoadPersistedConfig();
        config.LastProject = projectRoot;

        config.RecentProjects.RemoveAll(p =>
            string.Equals(p.Path, projectRoot, StringComparison.OrdinalIgnoreCase));

        config.RecentProjects.Insert(0, new RecentProject
        {
            Path = projectRoot,
            Name = Path.GetFileName(projectRoot),
            LastOpened = DateTime.UtcNow
        });

        if (config.RecentProjects.Count > 20)
            config.RecentProjects.RemoveRange(20, config.RecentProjects.Count - 20);

        SavePersistedConfig(config);
    }

    private static PersistedConfig LoadPersistedConfig()
    {
        try
        {
            if (File.Exists(ConfigFile))
            {
                var json = File.ReadAllText(ConfigFile);
                return JsonSerializer.Deserialize<PersistedConfig>(json) ?? new PersistedConfig();
            }
        }
        catch { /* corrupted config, start fresh */ }
        return new PersistedConfig();
    }

    private static void SavePersistedConfig(PersistedConfig config)
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigFile, json);
        }
        catch { /* best effort */ }
    }

    private static string NormalizeProjectRootCandidate(string inputPath)
    {
        var normalized = Path.GetFullPath(inputPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        var marker = $"{Path.DirectorySeparatorChar}.dna{Path.DirectorySeparatorChar}";
        var markerIndex = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex >= 0)
        {
            var beforeMarker = normalized[..markerIndex];
            if (!string.IsNullOrWhiteSpace(beforeMarker))
                return beforeMarker.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        if (normalized.EndsWith($"{Path.DirectorySeparatorChar}.dna", StringComparison.OrdinalIgnoreCase))
        {
            var parent = Path.GetDirectoryName(normalized);
            if (!string.IsNullOrWhiteSpace(parent))
                return parent;
        }

        var cursor = normalized;
        while (!string.IsNullOrWhiteSpace(cursor))
        {
            var dnaRoot = Path.Combine(cursor, ".dna", "dna");
            var projectJson = Path.Combine(cursor, ".dna", "project.json");
            if (Directory.Exists(dnaRoot) || File.Exists(projectJson))
                return cursor;

            // 对新项目初始化前场景，优先回退到最近的 git 根目录，避免把 src/engine 误选为项目根。
            if (Directory.Exists(Path.Combine(cursor, ".git")))
                return cursor;

            var parent = Path.GetDirectoryName(cursor);
            if (string.IsNullOrWhiteSpace(parent) || parent.Equals(cursor, StringComparison.Ordinal))
                break;
            cursor = parent;
        }

        return normalized;
    }
}

public record SetProjectResult(bool Success, string Message);

public class RecentProject
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTime LastOpened { get; set; }
}

public class PersistedConfig
{
    public string? LastProject { get; set; }
    public List<RecentProject> RecentProjects { get; set; } = [];
    public List<LlmProviderConfig> LlmProviders { get; set; } = [];
    public string? ActiveLlmProviderId { get; set; }
    public int? AgentToolThrottleMs { get; set; }
    public string? AgentGovernanceMode { get; set; }
}

/// <summary>
/// LLM 提供商配置 — 从 Agent.Chat 下沉到 Core.Config 以消除循环依赖
/// </summary>
public class LlmProviderConfig
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string ProviderType { get; set; } = "openai";
    public string ApiKey { get; set; } = "";
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string Model { get; set; } = "gpt-4o";

    public string EmbeddingBaseUrl { get; set; } = "";
    public string EmbeddingModel { get; set; } = "";
}

