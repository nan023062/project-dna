using Dna.Knowledge;
using Dna.Knowledge.Workspace.Models;
using Dna.Memory.Models;
using Dna.Workbench.Governance;
using Dna.Workbench.Tasks;

namespace Dna.App.Desktop.Services;

public interface IDesktopLocalWorkbenchClient
{
    Task<DesktopLocalRuntimeSnapshot> GetRuntimeSnapshotAsync(CancellationToken cancellationToken = default);

    Task<DesktopLocalRuntimeAccessSnapshot> GetAccessSnapshotAsync(CancellationToken cancellationToken = default);

    Task<WorkspaceDirectorySnapshot> GetWorkspaceTreeAsync(int maxDepth, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MemoryEntry>> QueryMemoriesAsync(int limit, int offset, CancellationToken cancellationToken = default);

    Task<MemoryEntry> RememberAsync(RememberRequest request, CancellationToken cancellationToken = default);

    Task<TopologyWorkbenchSnapshot> GetTopologySnapshotAsync(CancellationToken cancellationToken = default);

    Task SaveModuleAsync(string discipline, TopologyModuleDefinition module, CancellationToken cancellationToken = default);

    Task<TopologyModuleKnowledgeView?> GetModuleKnowledgeAsync(string nodeIdOrName, CancellationToken cancellationToken = default);

    Task<TopologyModuleKnowledgeView> SaveModuleKnowledgeAsync(
        TopologyModuleKnowledgeUpsertCommand command,
        CancellationToken cancellationToken = default);

    Task<TopologyModuleRelationsView?> GetModuleRelationsAsync(string nodeIdOrName, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkbenchTaskCandidate>> ResolveRequirementSupportAsync(
        WorkbenchRequirementRequest request,
        CancellationToken cancellationToken = default);

    Task<WorkbenchTaskStartResponse> StartTaskAsync(
        WorkbenchTaskRequest request,
        CancellationToken cancellationToken = default);

    Task<WorkbenchTaskEndResponse> EndTaskAsync(
        WorkbenchTaskResult request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkbenchActiveTaskSnapshot>> ListActiveTasksAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkbenchCompletedTaskSnapshot>> ListCompletedTasksAsync(
        int limit = 50,
        CancellationToken cancellationToken = default);

    Task<WorkbenchGovernanceContext> ResolveGovernanceAsync(
        WorkbenchGovernanceRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record DesktopLocalRuntimeSnapshot(
    string ProjectRoot,
    string MetadataRootPath,
    string MemoryStorePath,
    string SessionStorePath,
    string KnowledgeStorePath,
    int ModuleCount,
    int MemoryCount,
    object RuntimeLlmSummary);

public sealed record DesktopLocalRuntimeAccessSnapshot(
    bool Allowed,
    string Role,
    string EntryName,
    string RemoteIp,
    string? Note,
    string Reason);
