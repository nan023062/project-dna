namespace Dna.ExternalAgent.Contracts;

public interface IExternalAgentWorkspaceContext
{
    ExternalAgentWorkspaceContextSnapshot GetCurrentWorkspaceSnapshot();
}

public sealed class ExternalAgentWorkspaceContextSnapshot
{
    public string WorkspaceId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string WorkspaceRoot { get; init; } = string.Empty;
}
