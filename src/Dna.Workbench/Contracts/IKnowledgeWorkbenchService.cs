using Dna.Knowledge;
using Dna.Knowledge.Workspace.Models;
using Dna.Memory.Models;

namespace Dna.Workbench.Contracts;

public interface IKnowledgeWorkbenchService
{
    TopologyWorkbenchSnapshot GetTopologySnapshot();

    WorkspaceDirectorySnapshot GetWorkspaceSnapshot(string? relativePath = null);

    TopologyModuleKnowledgeView? GetModuleKnowledge(string nodeIdOrName);

    TopologyModuleKnowledgeView SaveModuleKnowledge(TopologyModuleKnowledgeUpsertCommand command);

    Task<MemoryEntry> RememberAsync(
        RememberRequest request,
        CancellationToken cancellationToken = default);

    Task<RecallResult> RecallAsync(
        RecallQuery query,
        CancellationToken cancellationToken = default);
}
