using Dna.Workbench.Contracts;

namespace Dna.Workbench.Runtime;

internal sealed class WorkbenchRuntimeService(
    IAgentRuntimeEventBus eventBus,
    ITopologyRuntimeProjectionService projectionService) : IWorkbenchRuntimeService
{
    public void Publish(WorkbenchRuntimeEvent runtimeEvent)
    {
        ArgumentNullException.ThrowIfNull(runtimeEvent);

        eventBus.Publish(runtimeEvent);
        projectionService.Apply(runtimeEvent);
    }

    public TopologyRuntimeProjectionSnapshot GetProjectionSnapshot()
        => projectionService.GetSnapshot();

    public void ResetProjection(string sessionId)
        => projectionService.Reset(sessionId);
}
