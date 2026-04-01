using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dna.Client.Services;

namespace Dna.Client.Desktop;

public sealed class DesktopProjectConfig
{
    private const string MetadataDirectoryName = ".project.dna";
    private const string ProjectConfigFileName = "project.json";
    private const string LlmConfigFileName = "llm.json";
    private const string WorkspaceConfigFileName = "client-workspaces.json";
    private const string WorkspaceSnapshotFileName = "client-workspaces.snapshot.json";
    private const string AgentShellDirectoryName = "agent-shell";
    private const string AgentShellStateFileName = "agent-shell-state.json";
    private const string AgentShellSnapshotFileName = "client-agent-shell.snapshot.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public required string ProjectRoot { get; init; }
    public required string ProjectName { get; init; }
    public required string ServerBaseUrl { get; init; }
    public required string MetadataRootPath { get; init; }
    public required string ConfigPath { get; init; }
    public required string LlmConfigPath { get; init; }
    public required string LogDirectoryPath { get; init; }
    public required string WorkspaceConfigPath { get; init; }
    public required string AgentShellRootPath { get; init; }
    public required string AgentShellStatePath { get; init; }

    public static DesktopProjectConfig Load(string projectRoot)
    {
        var normalizedRoot = Path.GetFullPath(projectRoot);
        if (!Directory.Exists(normalizedRoot))
            throw new InvalidOperationException($"项目目录不存在：{normalizedRoot}");

        var metadataRootPath = ResolveMetadataRootPath(normalizedRoot);
        var configPath = ResolveProjectConfigPath(normalizedRoot);
        TryMigrateLegacyConfig(metadataRootPath, configPath);

        if (!File.Exists(configPath))
            throw new InvalidOperationException($"未找到 Project DNA 项目配置：{configPath}");

        ProjectDnaConfig dto;
        try
        {
            var json = File.ReadAllText(configPath);
            dto = JsonSerializer.Deserialize<ProjectDnaConfig>(json, JsonOptions)
                  ?? throw new InvalidOperationException($"{ProjectConfigFileName} 内容为空。");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"{ProjectConfigFileName} JSON 解析失败：{ex.Message}");
        }

        var projectName = (dto.ProjectName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(projectName))
            throw new InvalidOperationException($"{ProjectConfigFileName} 缺少 projectName。");

        var serverBaseUrl = ClientRuntimeConstants.ApiBaseUrl;

        return new DesktopProjectConfig
        {
            ProjectRoot = normalizedRoot,
            ProjectName = projectName,
            ServerBaseUrl = serverBaseUrl,
            MetadataRootPath = metadataRootPath,
            ConfigPath = configPath,
            LlmConfigPath = ResolveLlmConfigPath(normalizedRoot),
            LogDirectoryPath = ResolveLogDirectoryPath(normalizedRoot),
            WorkspaceConfigPath = ResolveWorkspaceConfigPath(normalizedRoot),
            AgentShellRootPath = ResolveAgentShellRootPath(normalizedRoot),
            AgentShellStatePath = ResolveAgentShellStatePath(normalizedRoot)
        };
    }

    public void EnsureProjectScopedClientState()
    {
        Directory.CreateDirectory(MetadataRootPath);
        Directory.CreateDirectory(LogDirectoryPath);
        EnsureWorkspaceConfig();
        EnsureLlmConfig();
        EnsureAgentShellStorage();
    }

    public void EnsureWorkspaceConfig()
    {
        Directory.CreateDirectory(MetadataRootPath);
        if (File.Exists(WorkspaceConfigPath))
            return;

        if (TryCopyFromCandidates(
                WorkspaceConfigPath,
                ResolveWorkspaceSnapshotPath(ProjectRoot),
                ResolveLegacyProjectWorkspaceConfigPath(ProjectRoot),
                ResolveLegacyGlobalWorkspaceConfigPath()))
        {
            return;
        }

        var dir = Path.GetDirectoryName(WorkspaceConfigPath)
                  ?? throw new InvalidOperationException("无法确定 workspace 配置目录。");
        Directory.CreateDirectory(dir);
        File.WriteAllText(WorkspaceConfigPath, JsonSerializer.Serialize(CreateInitialWorkspaceState(), JsonOptions));
    }

    public void EnsureLlmConfig()
    {
        Directory.CreateDirectory(MetadataRootPath);
        Dna.Core.Config.RuntimeLlmConfigStore.LoadOrCreate(LlmConfigPath);
    }

    public void EnsureAgentShellStorage()
    {
        Directory.CreateDirectory(AgentShellRootPath);
        if (File.Exists(AgentShellStatePath))
            return;

        TryCopyFromCandidates(
            AgentShellStatePath,
            ResolveAgentShellSnapshotPath(ProjectRoot),
            ResolveLegacyGlobalAgentShellStatePath());
    }

    public static string ResolveMetadataRootPath(string projectRoot)
        => Path.Combine(Path.GetFullPath(projectRoot), MetadataDirectoryName);

    public static string ResolveProjectConfigPath(string projectRoot)
        => Path.Combine(ResolveMetadataRootPath(projectRoot), ProjectConfigFileName);

    public static string ResolveLogDirectoryPath(string projectRoot)
        => Path.Combine(ResolveMetadataRootPath(projectRoot), "logs");

    public static string ResolveLlmConfigPath(string projectRoot)
        => Path.Combine(ResolveMetadataRootPath(projectRoot), LlmConfigFileName);

    public static string ResolveWorkspaceConfigPath(string projectRoot)
        => Path.Combine(ResolveMetadataRootPath(projectRoot), WorkspaceConfigFileName);

    public static string ResolveAgentShellRootPath(string projectRoot)
        => Path.Combine(ResolveMetadataRootPath(projectRoot), AgentShellDirectoryName);

    public static string ResolveAgentShellStatePath(string projectRoot)
        => Path.Combine(ResolveAgentShellRootPath(projectRoot), AgentShellStateFileName);

    private WorkspaceConfigState CreateInitialWorkspaceState()
    {
        var mode = InferWorkspaceMode(ServerBaseUrl);
        return new WorkspaceConfigState
        {
            CurrentWorkspaceId = "default",
            Workspaces =
            [
                new WorkspaceConfigRecord
                {
                    Id = "default",
                    Name = ProjectName,
                    Mode = mode,
                    ServerBaseUrl = ServerBaseUrl,
                    WorkspaceRoot = ProjectRoot,
                    UpdatedAtUtc = DateTime.UtcNow
                }
            ]
        };
    }

    private static string InferWorkspaceMode(string serverBaseUrl)
    {
        _ = serverBaseUrl;
        return "personal";
    }

    private static string ResolveWorkspaceSnapshotPath(string projectRoot)
        => Path.Combine(ResolveMetadataRootPath(projectRoot), WorkspaceSnapshotFileName);

    private static string ResolveLegacyProjectWorkspaceConfigPath(string projectRoot)
    {
        var profileRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".dna",
            "client-desktop",
            "projects");

        var projectId = ComputeStableId(projectRoot);
        return Path.Combine(profileRoot, projectId, WorkspaceConfigFileName);
    }

    private static string ResolveLegacyGlobalWorkspaceConfigPath()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".dna",
            WorkspaceConfigFileName);

    private static string ResolveAgentShellSnapshotPath(string projectRoot)
        => Path.Combine(ResolveMetadataRootPath(projectRoot), AgentShellSnapshotFileName);

    private static string ResolveLegacyGlobalAgentShellStatePath()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".dna",
            "client-agent-shell",
            AgentShellStateFileName);

    private static string ComputeStableId(string projectRoot)
    {
        var normalized = Path.GetFullPath(projectRoot).ToLowerInvariant();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes[..8]).ToLowerInvariant();
    }

    private static void TryMigrateLegacyConfig(string metadataRootPath, string configPath)
    {
        if (Directory.Exists(metadataRootPath))
            return;

        var legacyFilePath = metadataRootPath;
        if (!File.Exists(legacyFilePath))
            return;

        var legacyContent = File.ReadAllText(legacyFilePath);

        try
        {
            File.Delete(legacyFilePath);
            Directory.CreateDirectory(metadataRootPath);
            File.WriteAllText(configPath, legacyContent);
        }
        catch
        {
            try
            {
                if (!File.Exists(legacyFilePath))
                    File.WriteAllText(legacyFilePath, legacyContent);
            }
            catch
            {
                // best effort restore
            }

            throw new InvalidOperationException(
                $"旧格式 {legacyFilePath} 迁移到 {configPath} 失败，请手动检查目录权限。");
        }
    }

    private static bool TryCopyFromCandidates(string targetPath, params string[] candidatePaths)
    {
        foreach (var candidatePath in candidatePaths)
        {
            if (string.IsNullOrWhiteSpace(candidatePath) || !File.Exists(candidatePath))
                continue;

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.Copy(candidatePath, targetPath, overwrite: false);
            return true;
        }

        return false;
    }

    private sealed class ProjectDnaConfig
    {
        public string? ProjectName { get; init; }
        public string? ServerBaseUrl { get; init; }
    }

    private sealed class WorkspaceConfigState
    {
        public string CurrentWorkspaceId { get; init; } = "default";
        public List<WorkspaceConfigRecord> Workspaces { get; init; } = [];
    }

    private sealed class WorkspaceConfigRecord
    {
        public string Id { get; init; } = "default";
        public string Name { get; init; } = string.Empty;
        public string Mode { get; init; } = "personal";
        public string ServerBaseUrl { get; init; } = string.Empty;
        public string WorkspaceRoot { get; init; } = string.Empty;
        public DateTime UpdatedAtUtc { get; init; }
    }
}
