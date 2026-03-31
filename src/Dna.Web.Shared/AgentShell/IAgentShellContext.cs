namespace Dna.Web.Shared.AgentShell;

public interface IAgentShellContext
{
    string HostKind { get; }

    Task<string> GenerateReplyAsync(AgentChatRequest request, CancellationToken cancellationToken = default);
}
