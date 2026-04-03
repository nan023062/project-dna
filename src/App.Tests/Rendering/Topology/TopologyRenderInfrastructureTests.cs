using Avalonia;
using Avalonia.Media;
using Dna.App.Desktop;
using Dna.App.Desktop.Topology;
using Xunit;

namespace App.Tests;

public sealed class TopologyRenderInfrastructureTests
{
    [Fact]
    public void TopologyFormattedTextCache_ShouldReuseEntriesWithinSameBucket()
    {
        var cache = new TopologyFormattedTextCache();

        var first = cache.GetOrCreate("Node", 12.04, Colors.Black, 120.04, 44.04, TextAlignment.Left, TextTrimming.CharacterEllipsis);
        var second = cache.GetOrCreate("Node", 12.03, Colors.Black, 120.01, 44.01, TextAlignment.Left, TextTrimming.CharacterEllipsis);
        var third = cache.GetOrCreate("Node", 13.2, Colors.Black, 120.01, 44.01, TextAlignment.Left, TextTrimming.CharacterEllipsis);

        Assert.Same(first, second);
        Assert.NotSame(first, third);
    }

    [Fact]
    public void TopologyEdgeRouteCache_ShouldReuseGraphRoutesForSameInputs()
    {
        var cache = new TopologyEdgeRouteCache();
        var options = new TopologyLayoutOptions();

        var first = cache.GetOrCreate(new Point(0, 0), new Point(100, 0), false, false, options);
        var second = cache.GetOrCreate(new Point(0, 0), new Point(100, 0), false, false, options);
        var third = cache.GetOrCreate(new Point(0, 0), new Point(160, 0), false, false, options);

        Assert.Same(first, second);
        Assert.NotSame(first, third);
        Assert.True(cache.Count >= 2);
    }

    [Fact]
    public void TopologyViewportCuller_ShouldCullOffscreenNodesAndDisconnectedOffscreenEdges()
    {
        var visibleNode = CreateNode("visible", "Visible");
        var offscreenLeft = CreateNode("off-left", "Off Left");
        var offscreenRight = CreateNode("off-right", "Off Right");

        var renderList = new TopologyRenderList(
            [
                new TopologyNodeRenderItem(visibleNode, new Point(0, 0), 0, false, false, false, true),
                new TopologyNodeRenderItem(offscreenLeft, new Point(2000, 0), 0, false, false, false, true),
                new TopologyNodeRenderItem(offscreenRight, new Point(2400, 0), 0, false, false, false, true)
            ],
            [
                new TopologyEdgeRenderItem(
                    new TopologyEdgeViewModel("visible", "off-left", "dependency", false),
                    visibleNode,
                    offscreenLeft,
                    new Point(0, 0),
                    new Point(2000, 0),
                    "dependency",
                    true),
                new TopologyEdgeRenderItem(
                    new TopologyEdgeViewModel("off-left", "off-right", "dependency", false),
                    offscreenLeft,
                    offscreenRight,
                    new Point(2000, 0),
                    new Point(2400, 0),
                    "dependency",
                    false)
            ],
            null);

        var culled = new TopologyViewportCuller().Cull(
            renderList,
            new Rect(0, 0, 800, 600),
            new TopologyViewportState(),
            new TopologyLayoutOptions());

        Assert.Equal(["visible"], culled.Nodes.Select(node => node.Node.Id));
        Assert.Single(culled.Edges);
        Assert.Equal("visible", culled.Edges[0].FromNode.Id);
        Assert.Equal("off-left", culled.Edges[0].ToNode.Id);
    }

    private static TopologyNodeViewModel CreateNode(string id, string label)
    {
        return new TopologyNodeViewModel(
            id,
            id,
            label,
            "Technical",
            "Module",
            "engineering",
            "Engineering",
            0,
            label,
            1);
    }
}
