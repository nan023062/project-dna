using Dna.ExternalAgent.Contracts;
using Dna.ExternalAgent.Models;

namespace Dna.ExternalAgent.Services;

internal sealed class ExternalAgentAdapterCatalog(IEnumerable<IExternalAgentAdapter> adapters) : IExternalAgentAdapterCatalog
{
    private readonly IReadOnlyList<IExternalAgentAdapter> _adapters = adapters.ToList();

    public IReadOnlyList<ExternalAgentAdapterDescriptor> ListAdapters()
        => _adapters.Select(item => item.Descriptor).ToList();

    public IExternalAgentAdapter? FindAdapter(string productId)
        => _adapters.FirstOrDefault(item =>
            string.Equals(item.Descriptor.ProductId, productId?.Trim(), StringComparison.OrdinalIgnoreCase));
}
