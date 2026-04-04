namespace Dna.Workbench.Contracts;

public interface IWorkbenchFacade
{
    IKnowledgeWorkbenchService Knowledge { get; }

    IWorkbenchToolService Tools { get; }

    IWorkbenchRuntimeService Runtime { get; }
}
