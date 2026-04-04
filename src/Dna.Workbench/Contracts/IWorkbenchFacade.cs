namespace Dna.Workbench.Contracts;

public interface IWorkbenchFacade
{
    IKnowledgeWorkbenchService Knowledge { get; }

    IAgentOrchestrationService Agent { get; }
}
