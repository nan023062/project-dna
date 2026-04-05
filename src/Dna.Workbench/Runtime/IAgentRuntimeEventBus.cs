namespace Dna.Workbench.Runtime;

public interface IAgentRuntimeEventBus
{
    void Publish(WorkbenchRuntimeEvent runtimeEvent);

    IDisposable Subscribe(Action<WorkbenchRuntimeEvent> handler);
}
