using Dna.ExternalAgent.Interfaces.Mcp;
using Dna.Knowledge;
using Dna.Knowledge.Workspace.Models;
using Dna.Memory.Models;
using Dna.Workbench.Contracts;
using Dna.Workbench.Governance;
using Dna.Workbench.Runtime;
using Dna.Workbench.Tasks;
using Dna.Workbench.Tooling;
using Xunit;

namespace App.Tests;

public sealed class ExternalAgentWorkbenchToolsTests
{
    [Fact]
    public async Task GetModuleKnowledge_ShouldReturnStructuredNotFoundError()
    {
        var tools = new ExternalAgentWorkbenchTools(new FakeWorkbenchFacade());

        var json = await tools.get_module_knowledge("missing-module");

        Assert.Contains("\"ok\": false", json, StringComparison.Ordinal);
        Assert.Contains("\"tool\": \"get_module_knowledge\"", json, StringComparison.Ordinal);
        Assert.Contains("\"code\": \"not_found\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Remember_ShouldReturnStructuredValidationError()
    {
        var tools = new ExternalAgentWorkbenchTools(new FakeWorkbenchFacade());

        var json = await tools.remember(
            content: "test",
            type: "bad-type",
            disciplines: "engineering",
            nodeType: "Technical");

        Assert.Contains("\"ok\": false", json, StringComparison.Ordinal);
        Assert.Contains("\"tool\": \"remember\"", json, StringComparison.Ordinal);
        Assert.Contains("\"code\": \"validation_error\"", json, StringComparison.Ordinal);
    }

    private sealed class FakeWorkbenchFacade : IWorkbenchFacade
    {
        public IKnowledgeWorkbenchService Knowledge { get; } = new FakeKnowledgeWorkbenchService();

        public IWorkbenchGovernanceService Governance { get; } = new FakeGovernanceService();

        public IWorkbenchTaskService Tasks { get; } = new FakeTaskService();

        public IWorkbenchToolService Tools { get; } = new FakeToolService();

        public IWorkbenchRuntimeService Runtime { get; } = new FakeRuntimeService();
    }

    private sealed class FakeKnowledgeWorkbenchService : IKnowledgeWorkbenchService
    {
        public TopologyWorkbenchSnapshot GetTopologySnapshot() => new();

        public WorkspaceDirectorySnapshot GetWorkspaceSnapshot(string? relativePath = null) => new();

        public TopologyModuleKnowledgeView? GetModuleKnowledge(string nodeIdOrName) => null;

        public TopologyModuleKnowledgeView SaveModuleKnowledge(TopologyModuleKnowledgeUpsertCommand command)
            => throw new NotSupportedException();

        public Task<MemoryEntry> RememberAsync(RememberRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new MemoryEntry { Id = "memory-1", Type = MemoryType.Episodic, Source = MemorySource.Ai, Content = request.Content });

        public Task<RecallResult> RecallAsync(RecallQuery query, CancellationToken cancellationToken = default)
            => Task.FromResult(new RecallResult());
    }

    private sealed class FakeGovernanceService : IWorkbenchGovernanceService
    {
        public Task<WorkbenchGovernanceContext> ResolveGovernanceAsync(
            WorkbenchGovernanceRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult(new WorkbenchGovernanceContext());
    }

    private sealed class FakeTaskService : IWorkbenchTaskService
    {
        public Task<IReadOnlyList<WorkbenchTaskCandidate>> ResolveRequirementSupportAsync(
            WorkbenchRequirementRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<WorkbenchTaskCandidate>>([]);

        public Task<WorkbenchTaskStartResponse> StartTaskAsync(
            WorkbenchTaskRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult(new WorkbenchTaskStartResponse());

        public Task<WorkbenchTaskEndResponse> EndTaskAsync(
            WorkbenchTaskResult result,
            CancellationToken cancellationToken = default) => Task.FromResult(new WorkbenchTaskEndResponse());

        public Task<IReadOnlyList<WorkbenchActiveTaskSnapshot>> ListActiveTasksAsync(
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<WorkbenchActiveTaskSnapshot>>([]);

        public Task<IReadOnlyList<WorkbenchCompletedTaskSnapshot>> ListCompletedTasksAsync(
            int limit = 50,
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<WorkbenchCompletedTaskSnapshot>>([]);
    }

    private sealed class FakeToolService : IWorkbenchToolService
    {
        public IReadOnlyList<WorkbenchToolDescriptor> ListTools() => [];

        public WorkbenchToolDescriptor? FindTool(string name) => null;

        public Task<WorkbenchToolInvocationResult> InvokeAsync(
            WorkbenchToolInvocationRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeRuntimeService : IWorkbenchRuntimeService
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
