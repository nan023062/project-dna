using Dna.App.Desktop;
using Xunit;

namespace App.Tests;

public sealed class TopologyGraphControlTests
{
    [Fact]
    public void TopologyGraph_ShouldNavigateAsProjectDepartmentModuleStateMachine()
    {
        var control = new TopologyGraphControl();
        var (nodes, edges) = TopologyGraphFixture.CreateHierarchyGraph();

        control.SetTopology(nodes, edges);

        Assert.Equal(["project"], control.VisibleNodeIds);
        Assert.True(control.CanNavigateInto("project"));
        Assert.Equal(2, control.GetChildCount("project"));

        Assert.True(control.NavigateInto("project"));
        Assert.Equal("project", control.ViewRootId);
        Assert.Equal(3, control.VisibleNodeIds.Count);
        Assert.Contains("project", control.VisibleNodeIds);
        Assert.Contains("__dept__:engineering", control.VisibleNodeIds);
        Assert.Contains("__dept__:art", control.VisibleNodeIds);
        Assert.DoesNotContain("team-root", control.VisibleNodeIds);
        Assert.Contains(
            control.VisibleEdges,
            edge => edge.Relation == "dependency" &&
                    edge.From == "__dept__:engineering" &&
                    edge.To == "__dept__:art");
        Assert.Contains(
            control.VisibleEdges,
            edge => edge.Relation == "collaboration" &&
                    edge.From == "__dept__:engineering" &&
                    edge.To == "__dept__:art");

        Assert.True(control.NavigateInto("__dept__:engineering"));
        Assert.Equal("__dept__:engineering", control.ViewRootId);
        Assert.Equal(["__dept__:engineering", "app-module"], control.VisibleNodeIds);

        Assert.True(control.NavigateInto("app-module"));
        Assert.Equal("app-module", control.ViewRootId);
        Assert.Equal(["app-module", "app-architecture"], control.VisibleNodeIds);

        Assert.True(control.NavigateUp());
        Assert.Equal("__dept__:engineering", control.ViewRootId);
        Assert.Equal(["__dept__:engineering", "app-module"], control.VisibleNodeIds);
    }
}
