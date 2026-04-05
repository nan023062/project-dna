using Dna.ExternalAgent.Models;

namespace Dna.ExternalAgent.Contracts;

public interface IExternalAgentAdapterCatalog
{
    IReadOnlyList<ExternalAgentAdapterDescriptor> ListAdapters();

    IExternalAgentAdapter? FindAdapter(string productId);
}
