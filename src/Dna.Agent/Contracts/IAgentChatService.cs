using Dna.Agent.Models;

namespace Dna.Agent.Contracts;

public interface IAgentChatService
{
    Task<AgentProviderState> GetProviderStateAsync(CancellationToken cancellationToken = default);

    Task<AgentProviderDescriptor?> SetActiveProviderAsync(
        string providerId,
        CancellationToken cancellationToken = default);

    Task<AgentChatSendResult> SendAsync(
        AgentChatSendRequest request,
        CancellationToken cancellationToken = default);

    Task<AgentChatSessionRecord?> GetSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgentChatSessionSummary>> ListSessionsAsync(CancellationToken cancellationToken = default);

    Task SaveSessionAsync(
        AgentChatSessionRecord session,
        CancellationToken cancellationToken = default);
}
