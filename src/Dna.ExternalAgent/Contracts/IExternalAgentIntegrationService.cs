using Dna.ExternalAgent.Models;

namespace Dna.ExternalAgent.Contracts;

public interface IExternalAgentIntegrationService
{
    IReadOnlyList<ExternalAgentAdapterDescriptor> ListAdapters();

    ExternalAgentPackageResult BuildPackage(ExternalAgentPackageRequest request);
}
