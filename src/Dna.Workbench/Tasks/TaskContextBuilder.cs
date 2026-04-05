using Dna.Knowledge;
using Dna.Memory.Models;

namespace Dna.Workbench.Tasks;

internal sealed class TaskContextBuilder(
    ITopoGraphApplicationService topology,
    IMemoryEngine memory) : ITaskContextBuilder
{
    public Task<WorkbenchTaskContext> BuildAsync(
        WorkbenchTaskRequest request,
        ModuleLock lease,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        topology.BuildTopology();

        var target = topology.GetModuleKnowledge(request.ModuleIdOrName)
            ?? throw new InvalidOperationException($"Module not found: {request.ModuleIdOrName}");

        var relations = topology.GetModuleRelations(target.NodeId);
        var visibleModules = BuildVisibleModules(target, relations);
        var outgoingRelations = BuildRelations(relations?.Outgoing);
        var incomingRelations = BuildRelations(relations?.Incoming);
        var collaborationContexts = BuildCrossWorkContexts(target.Name, topology.GetCrossWorksForModule(target.Name));
        var relevantMemories = memory.QueryMemories(new MemoryFilter
        {
            NodeId = target.NodeId,
            Freshness = FreshnessFilter.FreshAndAging,
            Limit = 20
        });

        return Task.FromResult(new WorkbenchTaskContext
        {
            TaskId = lease.TaskId,
            AgentId = request.AgentId,
            Type = request.Type,
            Goal = request.Goal,
            ModuleId = target.NodeId,
            ModuleName = target.Name,
            WorkspaceBoundary = string.Join(", ", BuildModulePaths(target)),
            WorkspaceScope = new WorkbenchTaskWorkspaceScope
            {
                WritableScopes =
                [
                    new WorkbenchTaskPathScope
                    {
                        ModuleId = target.NodeId,
                        ModuleName = target.Name,
                        Paths = BuildModulePaths(target)
                    }
                ],
                ReadableScopes =
                [
                    new WorkbenchTaskPathScope
                    {
                        ModuleId = target.NodeId,
                        ModuleName = target.Name,
                        Paths = BuildModulePaths(target)
                    },
                    .. visibleModules
                        .Where(item => item.ManagedPaths.Count > 0)
                        .Select(item => new WorkbenchTaskPathScope
                        {
                            ModuleId = string.IsNullOrWhiteSpace(item.NodeId) ? item.Name : item.NodeId,
                            ModuleName = item.Name,
                            Paths = item.ManagedPaths
                        })
                ],
                ContractOnlyScopes =
                [
                    .. visibleModules
                        .Select(item => new WorkbenchTaskContractScope
                        {
                            ModuleId = string.IsNullOrWhiteSpace(item.NodeId) ? item.Name : item.NodeId,
                            ModuleName = item.Name,
                            Level = item.Level,
                            ContractContent = item.ContractContent,
                            PublicApi = item.PublicApi,
                            Constraints = item.Constraints
                        })
                ]
            },
            TargetModule = new WorkbenchTaskContextModule
            {
                NodeId = target.NodeId,
                Name = target.Name,
                Summary = target.Summary,
                Boundary = target.Boundary,
                ManagedPaths = target.ManagedPaths,
                PublicApi = target.PublicApi,
                Constraints = target.Constraints,
                DeclaredDependencies = target.DeclaredDependencies,
                ComputedDependencies = target.ComputedDependencies,
                Identity = target.Knowledge.Identity,
                Facts = target.Knowledge.Facts,
                ActiveTasks = target.Knowledge.ActiveTasks
            },
            VisibleModules = visibleModules,
            OutgoingRelations = outgoingRelations,
            IncomingRelations = incomingRelations,
            CollaborationContexts = collaborationContexts,
            RelevantMemories = relevantMemories,
            Lease = lease
        });
    }

    private List<WorkbenchVisibleModuleContext> BuildVisibleModules(
        TopologyModuleKnowledgeView target,
        TopologyModuleRelationsView? relations)
    {
        var result = new List<WorkbenchVisibleModuleContext>();

        foreach (var dependency in relations?.Outgoing
                     .Where(item => item.Type == TopologyRelationType.Dependency)
                     .DistinctBy(item => item.ToId) ?? [])
        {
            var context = topology.GetModuleContext(dependency.ToName, target.Name, [target.Name]);
            var dependencyModule = topology.GetModuleKnowledge(dependency.ToId) ?? topology.GetModuleKnowledge(dependency.ToName);
            result.Add(new WorkbenchVisibleModuleContext
            {
                NodeId = dependency.ToId,
                Name = dependency.ToName,
                Level = context.Level,
                Summary = context.Summary,
                ContractContent = context.ContractContent,
                IdentityContent = context.IdentityContent,
                LessonsContent = context.LessonsContent,
                ActiveContent = context.ActiveContent,
                Boundary = context.Boundary,
                ManagedPaths = dependencyModule == null ? [] : BuildModulePaths(dependencyModule),
                PublicApi = context.PublicApi ?? [],
                Constraints = context.Constraints ?? []
            });
        }

        return result;
    }

    private static List<string> BuildModulePaths(TopologyModuleKnowledgeView module)
    {
        var paths = new List<string>();
        if (!string.IsNullOrWhiteSpace(module.RelativePath))
            paths.Add(module.RelativePath);

        paths.AddRange(module.ManagedPaths.Where(path => !string.IsNullOrWhiteSpace(path)));
        return paths
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<WorkbenchTaskRelation> BuildRelations(
        IReadOnlyList<TopologyModuleRelationView>? relations)
    {
        return relations?
            .Select(item => new WorkbenchTaskRelation
            {
                FromId = item.FromId,
                FromName = item.FromName,
                ToId = item.ToId,
                ToName = item.ToName,
                Type = item.Type,
                IsComputed = item.IsComputed,
                Label = item.Label
            })
            .ToList() ?? [];
    }

    private static List<WorkbenchTaskCrossWorkContext> BuildCrossWorkContexts(
        string targetModuleName,
        IReadOnlyList<CrossWork> crossWorks)
    {
        return crossWorks
            .Select(crossWork =>
            {
                var targetParticipant = crossWork.Participants.FirstOrDefault(participant =>
                    string.Equals(participant.ModuleName, targetModuleName, StringComparison.OrdinalIgnoreCase));

                return new WorkbenchTaskCrossWorkContext
                {
                    CrossWorkId = crossWork.Id,
                    CrossWorkName = crossWork.Name,
                    Description = crossWork.Description,
                    Feature = crossWork.Feature,
                    TargetRole = targetParticipant?.Role,
                    Participants = crossWork.Participants
                        .Select(participant => new WorkbenchTaskCrossWorkParticipant
                        {
                            ModuleName = participant.ModuleName,
                            ModuleId = null,
                            Role = participant.Role,
                            Contract = participant.Contract,
                            ContractType = participant.ContractType,
                            Deliverable = participant.Deliverable
                        })
                        .ToList()
                };
            })
            .ToList();
    }
}
