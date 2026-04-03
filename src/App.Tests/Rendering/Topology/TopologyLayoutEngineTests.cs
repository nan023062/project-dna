using Avalonia;
using Dna.App.Desktop.Topology;
using Xunit;

namespace App.Tests;

public sealed class TopologyLayoutEngineTests
{
    [Fact]
    public void ScopedLayoutEngine_ShouldKeepViewRootPinnedAndChildrenBelow()
    {
        var (nodes, edges) = TopologyGraphFixture.CreateHierarchyGraph();
        var scene = TopologyScene.Create(nodes, edges);
        var visible = scene.ResolveVisibleGraph(new TopologyFilterState(), "app-module");
        var engine = new ScopedTopologyLayoutEngine();

        var layout = engine.Layout(visible, scene, new TopologyLayoutOptions(), "app-module");

        Assert.Equal(new Point(0, -188), layout.NodeCenters["app-module"]);
        Assert.True(layout.NodeCenters["app-architecture"].Y > layout.NodeCenters["app-module"].Y);
    }

    [Fact]
    public void LayeredLayoutEngine_ShouldPlaceLowerLayersBelowHigherLayers()
    {
        var (nodes, edges) = TopologyGraphFixture.CreateHierarchyGraph();
        var scene = TopologyScene.Create(nodes, edges);
        var visible = scene.ResolveVisibleGraph(new TopologyFilterState(), "project");
        var engine = new LayeredTopologyLayoutEngine();

        var layout = engine.Layout(visible, scene, new TopologyLayoutOptions(), null);

        Assert.True(layout.NodeCenters["project"].Y <= layout.NodeCenters["__dept__:engineering"].Y);
        Assert.True(layout.NodeCenters["project"].Y <= layout.NodeCenters["__dept__:art"].Y);
    }
}
