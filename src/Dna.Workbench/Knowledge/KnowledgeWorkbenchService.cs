using Dna.Core.Config;
using Dna.Knowledge;
using Dna.Knowledge.Workspace;
using Dna.Knowledge.Workspace.Models;
using Dna.Memory.Models;
using Dna.Workbench.Contracts;

namespace Dna.Workbench.Knowledge;

internal sealed class KnowledgeWorkbenchService(
    ProjectConfig projectConfig,
    IWorkspaceEngine workspace,
    ITopoGraphApplicationService topology,
    IMemoryEngine memory) : IKnowledgeWorkbenchService
{
    public TopologyWorkbenchSnapshot GetTopologySnapshot()
    {
        topology.BuildTopology();
        return topology.GetWorkbenchSnapshot();
    }

    public WorkspaceDirectorySnapshot GetWorkspaceSnapshot(string? relativePath = null)
    {
        var projectRoot = ResolveProjectRoot();
        var topologyContext = topology.GetWorkspaceContext();

        return string.IsNullOrWhiteSpace(relativePath)
            ? workspace.GetRootSnapshot(projectRoot, topologyContext)
            : workspace.GetDirectorySnapshot(projectRoot, relativePath, topologyContext);
    }

    public TopologyModuleKnowledgeView? GetModuleKnowledge(string nodeIdOrName)
    {
        topology.BuildTopology();
        return topology.GetModuleKnowledge(nodeIdOrName);
    }

    public TopologyModuleKnowledgeView SaveModuleKnowledge(TopologyModuleKnowledgeUpsertCommand command)
    {
        topology.BuildTopology();
        return topology.SaveModuleKnowledge(command);
    }

    public Task<MemoryEntry> RememberAsync(
        RememberRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return memory.RememberAsync(request);
    }

    public Task<RecallResult> RecallAsync(
        RecallQuery query,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return memory.RecallAsync(query);
    }

    private string ResolveProjectRoot()
    {
        if (!projectConfig.HasProject)
            throw new InvalidOperationException("Workbench requires an active workspace before accessing workspace data.");

        return projectConfig.DefaultProjectRoot;
    }
}
