using Dna.App.Desktop;
using Dna.App.Desktop.Topology;
using Xunit;

namespace App.Tests;

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
        Assert.Equal(["project", "__dept__:engineering", "app-module"], scene.BuildScopeTrailIds("app-module"));

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

        Assert.Equal(["__dept__:engineering", "app-module"], visible.Nodes.Select(node => node.Id));
        Assert.Contains(
            visible.Edges,
            edge => edge.Relation == "dependency" &&
                    edge.From == "app-module" &&
                    edge.To == "__dept__:engineering");
        Assert.Contains(
            visible.Edges,
            edge => edge.Relation == "collaboration" &&
                    edge.From == "app-module" &&
                    edge.To == "__dept__:engineering");
    }
}
