using Dna.Workbench.Runtime;

namespace Dna.Workbench.Contracts;

public interface IWorkbenchRuntimeService
{
    void Publish(WorkbenchRuntimeEvent runtimeEvent);

    TopologyRuntimeProjectionSnapshot GetProjectionSnapshot();

    void ResetProjection(string sessionId);
}
