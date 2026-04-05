using Dna.ExternalAgent.Models;
using Dna.Workbench.Tooling;

namespace Dna.ExternalAgent.Contracts;

public interface IExternalAgentToolingService
{
    IReadOnlyList<WorkbenchToolDescriptor> ListMcpTools();

    IReadOnlyList<ExternalAgentToolingTargetStatus> GetTargetStatuses(
        string workspaceRoot,
        string mcpEndpoint,
        string serverName);

    ExternalAgentToolingTargetStatus GetTargetStatus(
        string productId,
        string workspaceRoot,
        string mcpEndpoint,
        string serverName);

    ExternalAgentToolingInstallReport InstallTarget(
        string productId,
        string workspaceRoot,
        string mcpEndpoint,
        string serverName,
        bool replaceExisting);
}
