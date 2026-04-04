using Dna.ExternalAgent.Contracts;
using Dna.ExternalAgent.DependencyInjection;
using Dna.ExternalAgent.Models;
using Dna.Knowledge;
using Dna.Knowledge.Workspace.Models;
using Dna.Memory.Models;
using Dna.Workbench.Contracts;
using Dna.Workbench.DependencyInjection;
using Dna.Workbench.Runtime;
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
            .AddWorkbench()
            .AddSingleton<IKnowledgeWorkbenchService, FakeKnowledgeWorkbenchService>()
            .AddSingleton<IWorkbenchRuntimeService, FakeWorkbenchRuntimeService>()
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
            .AddWorkbench()
            .AddSingleton<IKnowledgeWorkbenchService, FakeKnowledgeWorkbenchService>()
            .AddSingleton<IWorkbenchRuntimeService, FakeWorkbenchRuntimeService>()
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
    }

    private sealed class FakeKnowledgeWorkbenchService : IKnowledgeWorkbenchService
    {
        public TopologyWorkbenchSnapshot GetTopologySnapshot() => new();

        public WorkspaceDirectorySnapshot GetWorkspaceSnapshot(string? relativePath = null) => new();

        public TopologyModuleKnowledgeView? GetModuleKnowledge(string nodeIdOrName) => null;

        public TopologyModuleKnowledgeView SaveModuleKnowledge(TopologyModuleKnowledgeUpsertCommand command) => new();

        public Task<MemoryEntry> RememberAsync(RememberRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new MemoryEntry());

        public Task<RecallResult> RecallAsync(RecallQuery query, CancellationToken cancellationToken = default)
            => Task.FromResult(new RecallResult());
    }

    private sealed class FakeWorkbenchRuntimeService : IWorkbenchRuntimeService
    {
        public void Publish(WorkbenchRuntimeEvent runtimeEvent)
        {
        }

        public TopologyRuntimeProjectionSnapshot GetProjectionSnapshot() => new();

        public void ResetProjection(string sessionId)
        {
        }
    }
}
