using Dna.Workbench.Models.Agent;

namespace Dna.Workbench.Contracts;

public interface IAgentOrchestrationService
{
    Task<AgentSessionSnapshot> StartSessionAsync(
        AgentTaskRequest request,
        CancellationToken cancellationToken = default);

    AgentSessionSnapshot? GetSession(string sessionId);

    IReadOnlyList<AgentSessionSnapshot> ListSessions();

    Task<bool> CancelSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default);
}
