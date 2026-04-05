using Dna.ExternalAgent.Contracts;
using Dna.ExternalAgent.DependencyInjection;
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
        var status = tooling.GetTargetStatus("codex", _workspaceRoot, "http://localhost:5052/mcp", "agentic-os");

        Assert.Contains(report.Warnings, warning => warning.Contains("严格拓扑模式", StringComparison.Ordinal));
        Assert.Equal(3, report.WrittenFiles.Count);
        Assert.True(File.Exists(Path.Combine(_workspaceRoot, ".codex", "config.toml")));
        Assert.True(File.Exists(Path.Combine(_workspaceRoot, ".codex", "prompts", "agentic-os-topology.md")));
        Assert.True(File.Exists(Path.Combine(_workspaceRoot, ".codex", "agents", "agentic-os-topology.md")));
        Assert.True(status.McpConfigured);
        Assert.True(status.Installed);
    }

    [Fact]
    public void InstallTarget_ShouldSkipManagedFiles_WhenReplaceExistingIsFalse()
    {
        Directory.CreateDirectory(_workspaceRoot);
        var tooling = CreateToolingService();

        var firstReport = tooling.InstallTarget("cursor", _workspaceRoot, "http://localhost:5052/mcp", "agentic-os", replaceExisting: true);
        var promptPath = Path.Combine(_workspaceRoot, ".cursor", "rules", "agentic-os-topology.mdc");
        var agentPath = Path.Combine(_workspaceRoot, ".cursor", "agents", "agentic-os-topology.md");
        File.WriteAllText(promptPath, "custom prompt");
        File.WriteAllText(agentPath, "custom agent");

        var secondReport = tooling.InstallTarget("cursor", _workspaceRoot, "http://localhost:5052/mcp", "agentic-os", replaceExisting: false);

        Assert.Contains(promptPath, secondReport.SkippedFiles);
        Assert.Contains(agentPath, secondReport.SkippedFiles);
        Assert.Equal("custom prompt", File.ReadAllText(promptPath));
        Assert.Equal("custom agent", File.ReadAllText(agentPath));
    }

    [Theory]
    [InlineData("claude-code")]
    [InlineData("copilot")]
    public void InstallTarget_ShouldReportNonMcpTargets_AsInstalledWithoutMcp(string productId)
    {
        Directory.CreateDirectory(_workspaceRoot);
        var tooling = CreateToolingService();

        var report = tooling.InstallTarget(productId, _workspaceRoot, "http://localhost:5052/mcp", "agentic-os", replaceExisting: true);
        var status = tooling.GetTargetStatus(productId, _workspaceRoot, "http://localhost:5052/mcp", "agentic-os");

        Assert.NotEmpty(report.WrittenFiles);
        Assert.True(status.Installed);
        Assert.False(status.McpConfigured);
        Assert.False(status.Integration.RequiresMcp);
        Assert.True(status.Integration.Configured);
    }

    private static IExternalAgentToolingService CreateToolingService()
    {
        return new ServiceCollection()
            .AddLogging()
            .AddExternalAgent()
            .BuildServiceProvider()
            .GetRequiredService<IExternalAgentToolingService>();
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspaceRoot))
            Directory.Delete(_workspaceRoot, recursive: true);
    }
}
