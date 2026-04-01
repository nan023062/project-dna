using Dna.Client.Desktop;
using Dna.Client.Desktop.Topology;
using Xunit;

namespace Client.Tests;

public sealed class TopologySceneTests
{
    [Fact]
    public void TopologyScene_ShouldCollapseCrossDepartmentRelationsAtProjectScope()
    {
        var (nodes, edges) = TopologyGraphFixture.CreateHierarchyGraph();
        var scene = TopologyScene.Create(nodes, edges);

        Assert.True(scene.CanNavigateInto("project"));
        Assert.Equal(2, scene.GetChildCount("project"));
        Assert.Equal(["project"], scene.BuildScopeTrailIds("project"));
        Assert.Equal(["project", "__dept__:engineering", "client-module"], scene.BuildScopeTrailIds("client-module"));

        var visible = scene.ResolveVisibleGraph(new TopologyFilterState(), "project");

        Assert.Equal(["project", "__dept__:art", "__dept__:engineering"], visible.Nodes.Select(node => node.Id));
        Assert.Contains(
            visible.Edges,
            edge => edge.Relation == "dependency" &&
                    edge.From == "__dept__:engineering" &&
                    edge.To == "__dept__:art");
        Assert.Contains(
            visible.Edges,
            edge => edge.Relation == "collaboration" &&
                    edge.From == "__dept__:engineering" &&
                    edge.To == "__dept__:art");
    }

    [Fact]
    public void TopologyScene_ShouldCollapseExternalRelationsBackToCurrentScope()
    {
        var (nodes, edges) = TopologyGraphFixture.CreateHierarchyGraph();
        var scene = TopologyScene.Create(nodes, edges);

        var visible = scene.ResolveVisibleGraph(new TopologyFilterState(), "__dept__:engineering");

        Assert.Equal(["__dept__:engineering", "client-module"], visible.Nodes.Select(node => node.Id));
        Assert.Contains(
            visible.Edges,
            edge => edge.Relation == "dependency" &&
                    edge.From == "client-module" &&
                    edge.To == "__dept__:engineering");
        Assert.Contains(
            visible.Edges,
            edge => edge.Relation == "collaboration" &&
                    edge.From == "client-module" &&
                    edge.To == "__dept__:engineering");
    }
}
