namespace Dna.Workbench.Contracts;

public interface IWorkbenchFacade
{
    IKnowledgeWorkbenchService Knowledge { get; }

    IWorkbenchGovernanceService Governance { get; }

    IWorkbenchTaskService Tasks { get; }

    IWorkbenchToolService Tools { get; }

    IWorkbenchRuntimeService Runtime { get; }
}
