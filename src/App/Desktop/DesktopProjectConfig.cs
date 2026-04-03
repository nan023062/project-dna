using System.Text.Json;
using Dna.App.Services;
using Dna.Core.Config;

namespace Dna.App.Desktop;

public sealed class DesktopProjectConfig
{
    private const string MetadataDirectoryName = ".agentic-os";
    private const string ProjectConfigFileName = "project.json";
    private const string LlmConfigFileName = "llm.json";
    private const string WorkspaceConfigFileName = "app-workspaces.json";
    private const string WorkspaceSnapshotFileName = "app-workspaces.snapshot.json";
    private const string AgentShellDirectoryName = "agent-shell";
    private const string AgentShellStateFileName = "agent-shell-state.json";
    private const string AgentShellSnapshotFileName = "app-agent-shell.snapshot.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public required string ProjectRoot { get; init; }
    public required string ProjectName { get; init; }
    public required string ServerBaseUrl { get; init; }
    public required string MetadataRootPath { get; init; }
    public required string MemoryRootPath { get; init; }
    public required string SessionRootPath { get; init; }
    public required string KnowledgeRootPath { get; init; }
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
            throw new InvalidOperationException($"Project directory does not exist: {normalizedRoot}");

        var metadataRootPath = ResolveMetadataRootPath(normalizedRoot);
        var configPath = ResolveProjectConfigPath(normalizedRoot);
        var dto = TryReadOptionalConfig(configPath);
        var projectName = ResolveProjectName(dto, normalizedRoot);

        return new DesktopProjectConfig
        {
            ProjectRoot = normalizedRoot,
            ProjectName = projectName,
            ServerBaseUrl = AppRuntimeConstants.ApiBaseUrl,
            MetadataRootPath = metadataRootPath,
            MemoryRootPath = ResolveMemoryRootPath(normalizedRoot),
            SessionRootPath = ResolveSessionRootPath(normalizedRoot),
            KnowledgeRootPath = ResolveKnowledgeRootPath(normalizedRoot),
            ConfigPath = configPath,
            LlmConfigPath = ResolveLlmConfigPath(normalizedRoot),
            LogDirectoryPath = ResolveLogDirectoryPath(normalizedRoot),
            WorkspaceConfigPath = ResolveWorkspaceConfigPath(normalizedRoot),
            AgentShellRootPath = ResolveAgentShellRootPath(normalizedRoot),
            AgentShellStatePath = ResolveAgentShellStatePath(normalizedRoot)
        };
    }

    public void EnsureProjectScopedAppState()
    {
        Directory.CreateDirectory(MetadataRootPath);
        Directory.CreateDirectory(MemoryRootPath);
        Directory.CreateDirectory(SessionRootPath);
        Directory.CreateDirectory(KnowledgeRootPath);
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
                ResolveWorkspaceSnapshotPath(ProjectRoot)))
        {
            return;
        }

        var dir = Path.GetDirectoryName(WorkspaceConfigPath)
                  ?? throw new InvalidOperationException("Unable to resolve workspace config directory.");
        Directory.CreateDirectory(dir);
        File.WriteAllText(WorkspaceConfigPath, JsonSerializer.Serialize(CreateInitialWorkspaceState(), JsonOptions));
    }

    public void EnsureLlmConfig()
    {
        Directory.CreateDirectory(MetadataRootPath);
        RuntimeLlmConfigStore.LoadOrCreate(LlmConfigPath);
    }

    public void EnsureAgentShellStorage()
    {
        Directory.CreateDirectory(AgentShellRootPath);
        if (File.Exists(AgentShellStatePath))
            return;

        TryCopyFromCandidates(
            AgentShellStatePath,
            ResolveAgentShellSnapshotPath(ProjectRoot));
    }

    public static string ResolveMetadataRootPath(string projectRoot)
        => Path.Combine(Path.GetFullPath(projectRoot), MetadataDirectoryName);

    public static string ResolveMemoryRootPath(string projectRoot)
        => ProjectConfig.ResolveMemoryStorePath(ResolveMetadataRootPath(projectRoot));

    public static string ResolveSessionRootPath(string projectRoot)
        => ProjectConfig.ResolveSessionStorePath(ResolveMetadataRootPath(projectRoot));

    public static string ResolveKnowledgeRootPath(string projectRoot)
        => ProjectConfig.ResolveKnowledgeStorePath(ResolveMetadataRootPath(projectRoot));

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

    private static string ResolveAgentShellSnapshotPath(string projectRoot)
        => Path.Combine(ResolveMetadataRootPath(projectRoot), AgentShellSnapshotFileName);

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

    private static AgenticOsConfig? TryReadOptionalConfig(string configPath)
    {
        if (!File.Exists(configPath))
            return null;

        try
        {
            var json = File.ReadAllText(configPath);
            return JsonSerializer.Deserialize<AgenticOsConfig>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static string ResolveProjectName(AgenticOsConfig? dto, string projectRoot)
    {
        var projectName = (dto?.ProjectName ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(projectName))
            return projectName;

        var directoryName = new DirectoryInfo(projectRoot).Name.Trim();
        return string.IsNullOrWhiteSpace(directoryName)
            ? "workspace"
            : directoryName;
    }

    private sealed class AgenticOsConfig
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
