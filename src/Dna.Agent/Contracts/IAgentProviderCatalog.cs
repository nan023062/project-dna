using Dna.Agent.Models;

namespace Dna.Agent.Contracts;

public interface IAgentProviderCatalog
{
    Task<AgentProviderState> GetProviderStateAsync(CancellationToken cancellationToken = default);

    Task<AgentProviderDescriptor?> SetActiveProviderAsync(
        string providerId,
        CancellationToken cancellationToken = default);
}
