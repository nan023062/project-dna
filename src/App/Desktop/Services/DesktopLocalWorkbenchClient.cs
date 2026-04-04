using Dna.Knowledge;
using Dna.Knowledge.Workspace;
using Dna.Knowledge.Workspace.Models;
using Dna.Memory.Models;
using Dna.Workbench.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Dna.App.Desktop.Services;

public sealed class DesktopLocalWorkbenchClient(EmbeddedAppHost host) : IDesktopLocalWorkbenchClient
{
    public Task<DesktopLocalRuntimeSnapshot> GetRuntimeSnapshotAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var projectConfig = GetRequiredService<Dna.Core.Config.ProjectConfig>();
        var topology = GetRequiredService<ITopoGraphApplicationService>();
        var memory = GetRequiredService<IMemoryEngine>();
        var llm = GetRequiredService<Dna.App.Services.AppProjectLlmConfigService>();

        EnsureReady(topology);
        var snapshot = topology.BuildTopology();

        return Task.FromResult(new DesktopLocalRuntimeSnapshot(
            ProjectRoot: projectConfig.DefaultProjectRoot,
            MetadataRootPath: projectConfig.MetadataRootPath,
            MemoryStorePath: projectConfig.MemoryStorePath,
            SessionStorePath: projectConfig.SessionStorePath,
            KnowledgeStorePath: projectConfig.KnowledgeStorePath,
            ModuleCount: snapshot.Nodes.Count,
            MemoryCount: memory.MemoryCount(),
            RuntimeLlmSummary: llm.GetSummary()));
    }

    public Task<DesktopLocalRuntimeAccessSnapshot> GetAccessSnapshotAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = GetRequiredService<Dna.Core.Config.ProjectConfig>();

        return Task.FromResult(new DesktopLocalRuntimeAccessSnapshot(
            Allowed: true,
            Role: "admin",
            EntryName: "local-runtime",
            RemoteIp: "127.0.0.1",
            Note: "single-process desktop runtime",
            Reason: string.Empty));
    }

    public Task<WorkspaceDirectorySnapshot> GetWorkspaceTreeAsync(int maxDepth, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var knowledge = GetRequiredService<IKnowledgeWorkbenchService>();
        var depth = Math.Clamp(maxDepth, 0, 8);
        return Task.FromResult(BuildWorkspaceTree(knowledge, relativePath: null, depth));
    }

    public Task<IReadOnlyList<MemoryEntry>> QueryMemoriesAsync(int limit, int offset, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var memory = GetRequiredService<IMemoryEngine>();
        var items = memory.QueryMemories(new MemoryFilter
        {
            Limit = Math.Clamp(limit, 1, 200),
            Offset = Math.Max(offset, 0),
            Freshness = FreshnessFilter.FreshAndAging
        });

        return Task.FromResult<IReadOnlyList<MemoryEntry>>(items);
    }

    public Task<MemoryEntry> RememberAsync(RememberRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var knowledge = GetRequiredService<IKnowledgeWorkbenchService>();
        return knowledge.RememberAsync(request, cancellationToken);
    }

    public Task<TopologyWorkbenchSnapshot> GetTopologySnapshotAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var knowledge = GetRequiredService<IKnowledgeWorkbenchService>();
        return Task.FromResult(knowledge.GetTopologySnapshot());
    }

    public Task SaveModuleAsync(string discipline, TopologyModuleDefinition module, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(discipline))
            throw new ArgumentException("discipline is required.", nameof(discipline));

        var topology = GetRequiredService<ITopoGraphApplicationService>();
        EnsureReady(topology);

        var effectiveDiscipline = discipline.Trim();
        if (module.IsCrossWorkModule)
        {
            var ownership = ComputeCrossWorkOwnership(module.Participants, topology.GetManagementSnapshot().Modules);
            effectiveDiscipline = ownership.discipline;
            module.Layer = ownership.layer;
        }
        else
        {
            topology.UpsertDiscipline(effectiveDiscipline, effectiveDiscipline, "coder", []);
        }

        topology.RegisterModule(effectiveDiscipline, module);
        topology.BuildTopology();
        return Task.CompletedTask;
    }

    public Task<TopologyModuleKnowledgeView?> GetModuleKnowledgeAsync(string nodeIdOrName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var knowledge = GetRequiredService<IKnowledgeWorkbenchService>();
        return Task.FromResult(knowledge.GetModuleKnowledge(nodeIdOrName));
    }

    public Task<TopologyModuleKnowledgeView> SaveModuleKnowledgeAsync(
        TopologyModuleKnowledgeUpsertCommand command,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var knowledge = GetRequiredService<IKnowledgeWorkbenchService>();
        return Task.FromResult(knowledge.SaveModuleKnowledge(command));
    }

    public Task<TopologyModuleRelationsView?> GetModuleRelationsAsync(string nodeIdOrName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var topology = GetRequiredService<ITopoGraphApplicationService>();
        EnsureReady(topology);
        return Task.FromResult(topology.GetModuleRelations(nodeIdOrName));
    }

    private T GetRequiredService<T>() where T : notnull
    {
        var services = host.Services
            ?? throw new InvalidOperationException("Desktop local runtime is not running.");

        return services.GetRequiredService<T>();
    }

    private WorkspaceDirectorySnapshot BuildWorkspaceTree(
        IKnowledgeWorkbenchService knowledge,
        string? relativePath,
        int remainingDepth)
    {
        var snapshot = knowledge.GetWorkspaceSnapshot(relativePath);
        return new WorkspaceDirectorySnapshot
        {
            ProjectRoot = snapshot.ProjectRoot,
            RelativePath = snapshot.RelativePath,
            Name = snapshot.Name,
            FullPath = snapshot.FullPath,
            Exists = snapshot.Exists,
            ScannedAtUtc = snapshot.ScannedAtUtc,
            DirectoryCount = snapshot.DirectoryCount,
            FileCount = snapshot.FileCount,
            Entries = snapshot.Entries
                .Select(entry => CloneWorkspaceEntry(knowledge, entry, remainingDepth - 1))
                .ToList()
        };
    }

    private WorkspaceFileNode CloneWorkspaceEntry(
        IKnowledgeWorkbenchService knowledge,
        WorkspaceFileNode entry,
        int remainingDepth)
    {
        List<WorkspaceFileNode>? children = null;
        if (entry.Kind == WorkspaceEntryKind.Directory &&
            entry.HasChildren &&
            remainingDepth >= 0)
        {
            var childSnapshot = knowledge.GetWorkspaceSnapshot(entry.Path);
            children = childSnapshot.Entries
                .Select(child => CloneWorkspaceEntry(knowledge, child, remainingDepth - 1))
                .ToList();
        }

        return new WorkspaceFileNode
        {
            Name = entry.Name,
            Path = entry.Path,
            ParentPath = entry.ParentPath,
            FullPath = entry.FullPath,
            Kind = entry.Kind,
            Status = entry.Status,
            StatusLabel = entry.StatusLabel,
            Badge = entry.Badge,
            Extension = entry.Extension,
            SizeBytes = entry.SizeBytes,
            LastModifiedUtc = entry.LastModifiedUtc,
            Exists = entry.Exists,
            HasChildren = entry.HasChildren,
            ChildDirectoryCount = entry.ChildDirectoryCount,
            ChildFileCount = entry.ChildFileCount,
            Ownership = entry.Ownership?.Clone(),
            Module = entry.Module?.Clone(),
            Descriptor = entry.Descriptor?.Clone(),
            Actions = entry.Actions.Clone(),
            Children = children
        };
    }

    private void EnsureReady(ITopoGraphApplicationService topology)
    {
        var config = GetRequiredService<Dna.Core.Config.ProjectConfig>();
        if (!config.HasProject)
            throw new InvalidOperationException("Project is not configured.");

        topology.BuildTopology();
    }

    private static (string discipline, int layer) ComputeCrossWorkOwnership(
        List<TopologyCrossWorkParticipantDefinition> participants,
        IEnumerable<TopologyModuleDefinition> modules)
    {
        if (participants is not { Count: > 0 })
            return ("root", 0);

        var participantNames = new HashSet<string>(participants.Select(p => p.ModuleName), StringComparer.OrdinalIgnoreCase);
        var disciplines = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var maxLayer = 0;

        foreach (var module in modules)
        {
            if (!participantNames.Contains(module.Name))
                continue;

            disciplines.Add(module.Discipline);
            if (module.Layer > maxLayer)
                maxLayer = module.Layer;
        }

        return disciplines.Count == 1 ? (disciplines.First(), maxLayer) : ("root", 0);
    }
}
