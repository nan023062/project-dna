using Dna.Workbench.Contracts;

namespace Dna.Workbench;

internal sealed class WorkbenchFacade(
    IKnowledgeWorkbenchService knowledge,
    IWorkbenchToolService tools,
    IWorkbenchRuntimeService runtime) : IWorkbenchFacade
{
    public IKnowledgeWorkbenchService Knowledge { get; } = knowledge;

    public IWorkbenchToolService Tools { get; } = tools;

    public IWorkbenchRuntimeService Runtime { get; } = runtime;
}
