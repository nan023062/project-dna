using Dna.ExternalAgent.Contracts;
using Dna.ExternalAgent.DependencyInjection;
using Dna.ExternalAgent.Models;
using Dna.Workbench.Tooling;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace App.Tests;

public sealed class ExternalAgentIntegrationServiceTests
{
    [Fact]
    public void ListAdapters_ShouldExposeDefaultExternalAgentProducts()
    {
        using var services = new ServiceCollection()
            .AddLogging()
            .AddExternalAgent()
            .BuildServiceProvider();

        var integration = services.GetRequiredService<IExternalAgentIntegrationService>();
        var adapters = integration.ListAdapters();

        Assert.Contains(adapters, item => item.ProductId == ExternalAgentConstants.ProductIds.Cursor);
        Assert.Contains(adapters, item => item.ProductId == ExternalAgentConstants.ProductIds.ClaudeCode);
        Assert.Contains(adapters, item => item.ProductId == ExternalAgentConstants.ProductIds.Codex);
        Assert.Contains(adapters, item => item.ProductId == ExternalAgentConstants.ProductIds.Copilot);
    }

    [Fact]
    public void BuildPackage_ShouldGenerateCursorPackage_WithTopologyRules()
    {
        using var services = new ServiceCollection()
            .AddLogging()
            .AddExternalAgent()
            .BuildServiceProvider();

        var integration = services.GetRequiredService<IExternalAgentIntegrationService>();
        var package = integration.BuildPackage(new ExternalAgentPackageRequest
        {
            ProductId = ExternalAgentConstants.ProductIds.Cursor,
            ServerName = "agentic-os",
            McpEndpoint = "http://127.0.0.1:5052/mcp",
            StrictTopologyMode = true
        });

        Assert.Equal(ExternalAgentConstants.ProductIds.Cursor, package.Adapter.ProductId);
        Assert.True(package.Policy.StrictMode);
        Assert.Contains(package.Policy.RequiredToolNames, name => name == ExternalAgentConstants.DefaultToolNames.GetTopology);
        Assert.Contains(package.ManagedFiles, file => file.RelativePath == ExternalAgentConstants.ManagedPaths.CursorMcp);
        Assert.Contains(package.ManagedFiles, file => file.RelativePath == ExternalAgentConstants.ManagedPaths.CursorRule);
        Assert.Contains(package.ManagedFiles, file => file.Content.Contains("先解析知识拓扑", StringComparison.Ordinal));
        Assert.Contains(package.RequiredTools, tool => tool.Name == WorkbenchToolConstants.ToolNames.StartTask);
    }
}
