using Dna.Workbench.Tooling;

namespace Dna.ExternalAgent.Contracts;

public interface IExternalAgentToolCatalogService
{
    IReadOnlyList<WorkbenchToolDescriptor> ListTools();
}
