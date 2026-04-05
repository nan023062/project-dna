using Dna.Knowledge;

namespace Dna.Memory.Store;

internal sealed class MemoryTopoGraphContextProvider : ITopoGraphContextProvider
{
    private readonly MemoryStore _store;

    public MemoryTopoGraphContextProvider(MemoryStore store)
    {
        _store = store;
    }

    public TopoGraphIdentitySnapshot? GetIdentitySnapshot(string nodeId) => _store.GetIdentitySnapshot(nodeId);
    public TopoGraphContextContent GetContextContent(string nodeId) => _store.GetContextContent(nodeId);
}
