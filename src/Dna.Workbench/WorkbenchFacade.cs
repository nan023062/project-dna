using Dna.Workbench.Contracts;

namespace Dna.Workbench;

internal sealed class WorkbenchFacade(
    IKnowledgeWorkbenchService knowledge,
    IAgentOrchestrationService agent) : IWorkbenchFacade
{
    public IKnowledgeWorkbenchService Knowledge { get; } = knowledge;

    public IAgentOrchestrationService Agent { get; } = agent;
}
