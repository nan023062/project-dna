using Dna.Workbench.Models.Agent;

namespace Dna.Workbench.Runtime;

public interface IAgentRuntimeEventBus
{
    void Publish(AgentTimelineEvent runtimeEvent);

    IDisposable Subscribe(Action<AgentTimelineEvent> handler);
}
