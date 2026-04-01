namespace Dna.Client.Desktop.Topology;

public readonly record struct TopologyRenderListCacheKey(
    int StructureRevision,
    int InteractionRevision);

public sealed class TopologyRenderListCache
{
    private TopologyRenderListCacheKey? _lastKey;
    private TopologyRenderList _cached = TopologyRenderList.Empty;

    public bool HasCachedValue => _lastKey.HasValue;

    public TopologyRenderList GetOrCreate(TopologyRenderListCacheKey key, Func<TopologyRenderList> factory)
    {
        if (_lastKey.HasValue && _lastKey.Value.Equals(key))
            return _cached;

        _cached = factory();
        _lastKey = key;
        return _cached;
    }

    public void Invalidate()
    {
        _lastKey = null;
        _cached = TopologyRenderList.Empty;
    }
}
