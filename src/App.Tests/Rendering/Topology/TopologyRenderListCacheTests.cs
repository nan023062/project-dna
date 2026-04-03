using Avalonia;
using Dna.App.Desktop;
using Dna.App.Desktop.Topology;
using Xunit;

namespace App.Tests;

public sealed class TopologyRenderListCacheTests
{
    [Fact]
    public void TopologyRenderListCache_ShouldReuseSameKeyResult()
    {
        var cache = new TopologyRenderListCache();
        var key = new TopologyRenderListCacheKey(1, 1);
        var buildCount = 0;

        var first = cache.GetOrCreate(key, () =>
        {
            buildCount++;
            return CreateRenderList("a");
        });

        var second = cache.GetOrCreate(key, () =>
        {
            buildCount++;
            return CreateRenderList("b");
        });

        Assert.Equal(1, buildCount);
        Assert.Same(first, second);
        Assert.Equal("a", second.Nodes[0].Node.Id);
    }

    [Fact]
    public void TopologyRenderListCache_ShouldRefreshWhenRevisionChanges()
    {
        var cache = new TopologyRenderListCache();
        var buildCount = 0;

        var first = cache.GetOrCreate(new TopologyRenderListCacheKey(1, 1), () =>
        {
            buildCount++;
            return CreateRenderList("first");
        });

        var second = cache.GetOrCreate(new TopologyRenderListCacheKey(2, 1), () =>
        {
            buildCount++;
            return CreateRenderList("second");
        });

        var third = cache.GetOrCreate(new TopologyRenderListCacheKey(2, 2), () =>
        {
            buildCount++;
            return CreateRenderList("third");
        });

        Assert.Equal(3, buildCount);
        Assert.NotSame(first, second);
        Assert.NotSame(second, third);
        Assert.Equal("third", third.Nodes[0].Node.Id);
    }

    [Fact]
    public void TopologyRenderListCache_ShouldDropCachedValueAfterInvalidate()
    {
        var cache = new TopologyRenderListCache();
        var buildCount = 0;
        var key = new TopologyRenderListCacheKey(1, 1);

        var first = cache.GetOrCreate(key, () =>
        {
            buildCount++;
            return CreateRenderList("first");
        });

        cache.Invalidate();

        var second = cache.GetOrCreate(key, () =>
        {
            buildCount++;
            return CreateRenderList("second");
        });

        Assert.Equal(2, buildCount);
        Assert.NotSame(first, second);
        Assert.Equal("second", second.Nodes[0].Node.Id);
    }

    private static TopologyRenderList CreateRenderList(string id)
    {
        var node = new TopologyNodeViewModel(
            id,
            id,
            id,
            "Technical",
            "Module",
            "engineering",
            "Engineering",
            0,
            id,
            1);

        return new TopologyRenderList(
            [new TopologyNodeRenderItem(node, new Point(0, 0), 0, false, false, false, true)],
            [],
            null);
    }
}
