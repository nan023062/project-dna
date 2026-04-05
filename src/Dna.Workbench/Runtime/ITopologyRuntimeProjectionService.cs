namespace Dna.Workbench.Runtime;

public interface ITopologyRuntimeProjectionService
{
    TopologyRuntimeProjectionSnapshot GetSnapshot();

    void Apply(WorkbenchRuntimeEvent runtimeEvent);

    void Reset(string sessionId);
}
