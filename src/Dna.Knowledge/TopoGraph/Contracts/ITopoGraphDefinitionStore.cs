using Dna.Knowledge.TopoGraph.Models.Registrations;

namespace Dna.Knowledge.TopoGraph.Contracts;

public interface ITopoGraphDefinitionStore
{
    void Initialize(string storePath);
    void Reload();
    TopologyModelDefinition LoadDefinition();
    void SaveDefinition(TopologyModelDefinition definition);
}
