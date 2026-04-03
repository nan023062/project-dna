using Dna.App.Desktop;
using Xunit;

namespace App.Tests;

public sealed class DesktopProjectConfigTests
{
    [Fact]
    public void Load_ShouldUseDirectoryBasedProjectConfig()
    {
        var projectRoot = CreateProjectRoot();
        var metadataRoot = Path.Combine(projectRoot, ".agentic-os");
        Directory.CreateDirectory(metadataRoot);
        File.WriteAllText(
            Path.Combine(metadataRoot, "project.json"),
            """
            {
              "projectName": "agentic-os",
              "serverBaseUrl": "http://127.0.0.1:5051"
            }
            """);

        var config = DesktopProjectConfig.Load(projectRoot);

        Assert.Equal(metadataRoot, config.MetadataRootPath);
        Assert.Equal(Path.Combine(metadataRoot, "memory"), config.MemoryRootPath);
        Assert.Equal(Path.Combine(metadataRoot, "knowledge"), config.KnowledgeRootPath);
        Assert.Equal(Path.Combine(metadataRoot, "project.json"), config.ConfigPath);
        
        var expectedLlmPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".agentic-os", "llm.json");
        Assert.Equal(expectedLlmPath, config.LlmConfigPath);
        
        Assert.Equal(Path.Combine(metadataRoot, "logs"), config.LogDirectoryPath);
    }

    [Fact]
    public void EnsureLlmConfig_ShouldMaterializeProjectMetadataFile()
    {
        var projectRoot = CreateProjectRoot();
        var metadataRoot = Path.Combine(projectRoot, ".agentic-os");
        Directory.CreateDirectory(metadataRoot);
        File.WriteAllText(
            Path.Combine(metadataRoot, "project.json"),
            """
            {
              "projectName": "agentic-os",
              "serverBaseUrl": "http://127.0.0.1:5051"
            }
            """);

        var config = DesktopProjectConfig.Load(projectRoot);
        config.EnsureLlmConfig();

        Assert.True(File.Exists(config.LlmConfigPath));
        var json = File.ReadAllText(config.LlmConfigPath);
        Assert.Contains("\"providers\"", json);
        Assert.Contains("\"purposes\"", json);
    }

    [Fact]
    public void EnsureProjectScopedAppState_ShouldMaterializeWorkspaceFromProjectSnapshots()
    {
        var projectRoot = CreateProjectRoot();
        var metadataRoot = Path.Combine(projectRoot, ".agentic-os");
        Directory.CreateDirectory(metadataRoot);
        File.WriteAllText(
            Path.Combine(metadataRoot, "project.json"),
            """
            {
              "projectName": "agentic-os",
              "serverBaseUrl": "http://127.0.0.1:5051"
            }
            """);
        File.WriteAllText(
            Path.Combine(metadataRoot, "app-workspaces.snapshot.json"),
            """
            {
              "currentWorkspaceId": "default",
              "workspaces": [
                {
                  "id": "default",
                  "name": "agentic-os",
                  "mode": "personal",
                  "serverBaseUrl": "http://127.0.0.1:5051",
                  "workspaceRoot": "D:\\GitRepository\\agentic-os",
                  "updatedAtUtc": "2026-04-01T03:26:31.5519464Z"
                }
              ]
            }
            """);

        var config = DesktopProjectConfig.Load(projectRoot);
        config.EnsureProjectScopedAppState();

        Assert.True(File.Exists(config.LlmConfigPath));
        Assert.True(Directory.Exists(config.MemoryRootPath));
        Assert.True(Directory.Exists(config.KnowledgeRootPath));
        Assert.True(Directory.Exists(config.LogDirectoryPath));
    }

    [Fact]
    public void Load_ShouldFallbackToDirectoryName_WhenProjectConfigDoesNotExist()
    {
        var projectRoot = CreateProjectRoot();

        var config = DesktopProjectConfig.Load(projectRoot);
        config.EnsureProjectScopedAppState();

        Assert.Equal(new DirectoryInfo(projectRoot).Name, config.ProjectName);
        Assert.False(File.Exists(config.ConfigPath));
        Assert.True(Directory.Exists(config.MemoryRootPath));
        Assert.True(Directory.Exists(config.KnowledgeRootPath));
        Assert.True(Directory.Exists(config.SessionRootPath));
        Assert.True(Directory.Exists(config.LogDirectoryPath));
    }

    private static string CreateProjectRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "dna-desktop-project-config-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
