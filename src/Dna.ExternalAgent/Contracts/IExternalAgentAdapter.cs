using Dna.ExternalAgent.Models;

namespace Dna.ExternalAgent.Contracts;

public interface IExternalAgentAdapter
{
    ExternalAgentAdapterDescriptor Descriptor { get; }

    ExternalAgentPackageResult BuildPackage(ExternalAgentPackageContext context);
}
