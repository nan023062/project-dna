using Dna.Workbench.Models.Agent;

namespace Dna.Workbench.Runtime;

public interface ITopologyRuntimeProjectionService
{
    TopologyRuntimeProjectionSnapshot GetSnapshot();

    void Apply(AgentTimelineEvent runtimeEvent);

    void Reset(string sessionId);
}
