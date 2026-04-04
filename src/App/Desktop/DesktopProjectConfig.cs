using Dna.App.Services;
using Dna.Core.Config;

namespace Dna.App.Desktop;

public sealed class DesktopProjectConfig
{
    private const string MetadataDirectoryName = ".agentic-os";
    private const string LlmConfigFileName = "llm.json";

    public required string ProjectRoot { get; init; }
    public required string ProjectName { get; init; }
    public required string ServerBaseUrl { get; init; }
    public required string MetadataRootPath { get; init; }
    public required string MemoryRootPath { get; init; }
    public required string SessionRootPath { get; init; }
    public required string KnowledgeRootPath { get; init; }
    public required string LlmConfigPath { get; init; }
    public required string LogDirectoryPath { get; init; }

    public static DesktopProjectConfig Load(string projectRoot)
    {
        var normalizedRoot = Path.GetFullPath(projectRoot);
        if (!Directory.Exists(normalizedRoot))
            throw new InvalidOperationException($"Project directory does not exist: {normalizedRoot}");

        var metadataRootPath = ResolveMetadataRootPath(normalizedRoot);
        var projectName = ResolveProjectName(normalizedRoot);

        return new DesktopProjectConfig
        {
            ProjectRoot = normalizedRoot,
            ProjectName = projectName,
            ServerBaseUrl = AppRuntimeConstants.ApiBaseUrl,
            MetadataRootPath = metadataRootPath,
            MemoryRootPath = ResolveMemoryRootPath(normalizedRoot),
            SessionRootPath = ResolveSessionRootPath(normalizedRoot),
            KnowledgeRootPath = ResolveKnowledgeRootPath(normalizedRoot),
            LlmConfigPath = ResolveLlmConfigPath(normalizedRoot),
            LogDirectoryPath = ResolveLogDirectoryPath(normalizedRoot)
        };
    }

    public void EnsureProjectScopedAppState()
    {
        Directory.CreateDirectory(MetadataRootPath);
        Directory.CreateDirectory(MemoryRootPath);
        Directory.CreateDirectory(SessionRootPath);
        Directory.CreateDirectory(KnowledgeRootPath);
        Directory.CreateDirectory(LogDirectoryPath);
        EnsureLlmConfig();
    }

    public void EnsureLlmConfig()
    {
        var dir = Path.GetDirectoryName(LlmConfigPath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);
            
        RuntimeLlmConfigStore.LoadOrCreate(LlmConfigPath);
    }

    public static string ResolveMetadataRootPath(string projectRoot)
        => Path.Combine(Path.GetFullPath(projectRoot), MetadataDirectoryName);

    public static string ResolveMemoryRootPath(string projectRoot)
        => ProjectConfig.ResolveMemoryStorePath(ResolveMetadataRootPath(projectRoot));

    public static string ResolveSessionRootPath(string projectRoot)
        => ProjectConfig.ResolveSessionStorePath(ResolveMetadataRootPath(projectRoot));

    public static string ResolveKnowledgeRootPath(string projectRoot)
        => ProjectConfig.ResolveKnowledgeStorePath(ResolveMetadataRootPath(projectRoot));

    public static string ResolveLogDirectoryPath(string projectRoot)
        => Path.Combine(ResolveMetadataRootPath(projectRoot), "logs");

    public static string ResolveLlmConfigPath(string projectRoot)
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(userProfile, MetadataDirectoryName, LlmConfigFileName);
    }

    private static string ResolveProjectName(string projectRoot)
    {
        var directoryName = new DirectoryInfo(projectRoot).Name.Trim();
        return string.IsNullOrWhiteSpace(directoryName)
            ? "workspace"
            : directoryName;
    }
}
