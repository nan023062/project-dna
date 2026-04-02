using Dna.App.Services.Tooling;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace App.Tests;

public sealed class AppIdeToolingServiceTests : IDisposable
{
    private readonly string _workspaceRoot = Path.Combine(Path.GetTempPath(), "dna-app-tooling-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void InstallTarget_ShouldCreateManagedFiles_AndReportInstalledStatus()
    {
        Directory.CreateDirectory(_workspaceRoot);
        var tooling = CreateToolingService();

        var report = tooling.InstallTarget("codex", _workspaceRoot, "http://localhost:5052/mcp", "agentic-os", replaceExisting: true);
        var status = tooling.GetStatus("codex", _workspaceRoot, "http://localhost:5052/mcp", "agentic-os");

        Assert.Empty(report.Warnings);
        Assert.Equal(3, report.WrittenFiles.Count);
        Assert.True(File.Exists(Path.Combine(_workspaceRoot, ".codex", "mcp.json")));
        Assert.True(File.Exists(Path.Combine(_workspaceRoot, ".codex", "prompts", "agentic-os-mcp-hook.md")));
        Assert.True(File.Exists(Path.Combine(_workspaceRoot, ".codex", "agents", "agentic-os-mcp-hooks.md")));
        Assert.True(status.McpConfigured);
        Assert.True(status.Installed);
    }

    [Fact]
    public void InstallTarget_ShouldSkipManagedFiles_WhenReplaceExistingIsFalse()
    {
        Directory.CreateDirectory(_workspaceRoot);
        var tooling = CreateToolingService();

        var firstReport = tooling.InstallTarget("cursor", _workspaceRoot, "http://localhost:5052/mcp", "agentic-os", replaceExisting: true);
        var promptPath = firstReport.Paths.PromptFile;
        var agentPath = firstReport.Paths.AgentFile;
        File.WriteAllText(promptPath, "custom prompt");
        File.WriteAllText(agentPath, "custom agent");

        var secondReport = tooling.InstallTarget("cursor", _workspaceRoot, "http://localhost:5052/mcp", "agentic-os", replaceExisting: false);

        Assert.Contains(promptPath, secondReport.SkippedFiles);
        Assert.Contains(agentPath, secondReport.SkippedFiles);
        Assert.Equal("custom prompt", File.ReadAllText(promptPath));
        Assert.Equal("custom agent", File.ReadAllText(agentPath));
    }

    private static AppIdeToolingService CreateToolingService()
    {
        return new ServiceCollection()
            .AddAppToolingServices()
            .BuildServiceProvider()
            .GetRequiredService<AppIdeToolingService>();
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspaceRoot))
            Directory.Delete(_workspaceRoot, recursive: true);
    }
}
