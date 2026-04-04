using Dna.Workbench.Contracts;

namespace Dna.Workbench;

internal sealed class WorkbenchFacade(
    IKnowledgeWorkbenchService knowledge,
    IWorkbenchGovernanceService governance,
    IWorkbenchTaskService tasks,
    IWorkbenchToolService tools,
    IWorkbenchRuntimeService runtime) : IWorkbenchFacade
{
    public IKnowledgeWorkbenchService Knowledge { get; } = knowledge;

    public IWorkbenchGovernanceService Governance { get; } = governance;

    public IWorkbenchTaskService Tasks { get; } = tasks;

    public IWorkbenchToolService Tools { get; } = tools;

    public IWorkbenchRuntimeService Runtime { get; } = runtime;
}
