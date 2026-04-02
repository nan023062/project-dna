namespace Dna.Client.Services.AgentShell;

public interface IAgentShellContext
{
    string HostKind { get; }

    Task<string> GenerateReplyAsync(AgentChatRequest request, CancellationToken cancellationToken = default);
}
