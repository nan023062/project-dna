using Dna.Knowledge;
using Dna.Workbench.Contracts;

namespace Dna.Workbench.Governance;

internal sealed class WorkbenchGovernanceService(
    IGovernanceEngine governance,
    ITopoGraphApplicationService topology) : IWorkbenchGovernanceService
{
    private static readonly IReadOnlyList<WorkbenchGovernanceRule> DefaultRules =
    [
        new()
        {
            Code = "dag",
            Title = "DAG 铁律",
            Description = "依赖必须保持有向无环；发现循环依赖时，优先通过提取接口或下沉公共逻辑解环。"
        },
        new()
        {
            Code = "gravity",
            Title = "引力法则",
            Description = "依赖只能从高层模块流向低层模块；禁止底层模块反向感知业务层。"
        },
        new()
        {
            Code = "srp",
            Title = "单一职责",
            Description = "模块行为超出其职责摘要时，应优先拆分或重新收口边界，而不是继续堆叠实现。"
        },
        new()
        {
            Code = "contract-first",
            Title = "契约优先",
            Description = "跨模块协作优先经过契约；对外暴露接口与模块知识必须保持同步。"
        },
        new()
        {
            Code = "dry-downward",
            Title = "DRY 下沉",
            Description = "平级模块出现重复轮子时，应优先提炼为公共能力并下沉到合适技术模块。"
        }
    ];

    public async Task<WorkbenchGovernanceContext> ResolveGovernanceAsync(
        WorkbenchGovernanceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        topology.BuildTopology();

        var scan = await governance.ScanAsync(new GovernanceScanRequest
        {
            Cadence = request.Cadence,
            Scope = request.Scope,
            NodeIdOrName = request.NodeIdOrName,
            ActiveWindow = request.ActiveWindow,
            IncludeDirectDependencies = request.IncludeDirectDependencies,
            MaxModules = request.MaxModules,
            MaxSuggestions = request.MaxSuggestions
        });

        cancellationToken.ThrowIfCancellationRequested();

        var modules = scan.CandidateModules
            .Select(candidate => BuildModuleContext(candidate))
            .Where(context => context != null)
            .Cast<WorkbenchGovernanceModuleContext>()
            .ToList();

        return new WorkbenchGovernanceContext
        {
            GeneratedAt = scan.GeneratedAt,
            Cadence = scan.Cadence,
            Scope = scan.Scope,
            ScopeNodeId = scan.ScopeNodeId,
            ScopeNodeName = scan.ScopeNodeName,
            Rules = DefaultRules,
            ActiveModules = scan.ActiveModules,
            Modules = modules,
            ArchitectureReport = scan.ArchitectureReport,
            EvolutionReport = scan.EvolutionReport
        };
    }

    private WorkbenchGovernanceModuleContext? BuildModuleContext(GovernanceCandidateModule candidate)
    {
        var knowledge = topology.GetModuleKnowledge(candidate.ModuleId)
            ?? topology.GetModuleKnowledge(candidate.ModuleName);
        if (knowledge == null)
            return null;

        return new WorkbenchGovernanceModuleContext
        {
            NodeId = knowledge.NodeId,
            Name = knowledge.Name,
            Type = knowledge.Type,
            Discipline = knowledge.Discipline,
            ParentId = knowledge.ParentId,
            Layer = knowledge.Layer,
            Summary = knowledge.Summary,
            Boundary = knowledge.Boundary,
            ManagedPaths = knowledge.ManagedPaths,
            DeclaredDependencies = knowledge.DeclaredDependencies,
            ComputedDependencies = knowledge.ComputedDependencies,
            PublicApi = knowledge.PublicApi,
            Constraints = knowledge.Constraints,
            KnowledgeIdentity = knowledge.Knowledge.Identity,
            KnowledgeFacts = knowledge.Knowledge.Facts,
            KnowledgeActiveTasks = knowledge.Knowledge.ActiveTasks,
            IsDirectlyActive = candidate.IsDirectlyActive,
            AddedByDependencyExpansion = candidate.AddedByDependencyExpansion,
            GovernanceReasons = candidate.Reasons
        };
    }
}
