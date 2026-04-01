using Dna.Client.Desktop;
using Xunit;

namespace Client.Tests;

public sealed class TopologyGraphControlTests
{
    [Fact]
    public void TopologyGraph_ShouldNavigateAsProjectDepartmentModuleStateMachine()
    {
        var control = new TopologyGraphControl();

        var project = new TopologyNodeViewModel(
            "project",
            "project",
            "Project",
            "Project",
            "Project",
            "root",
            "Project",
            0,
            "Project root",
            0);
        var engineering = new TopologyNodeViewModel(
            "__dept__:engineering",
            "__dept__:engineering",
            "Engineering",
            "Department",
            "Department",
            "engineering",
            "Engineering",
            0,
            "Engineering department",
            0);
        var art = new TopologyNodeViewModel(
            "__dept__:art",
            "__dept__:art",
            "Art",
            "Department",
            "Department",
            "art",
            "Art",
            0,
            "Art department",
            0);
        var rootTeam = new TopologyNodeViewModel(
            "team-root",
            "team-root",
            "Root Team",
            "Team",
            "Team",
            "root",
            "Root",
            0,
            "Cross-cutting team",
            0);
        var clientModule = new TopologyNodeViewModel(
            "client-module",
            "client-module-id",
            "Client",
            "Technical",
            "Module",
            "engineering",
            "Engineering",
            0,
            "Top-level client module",
            1,
            ParentModuleId: null,
            ChildModuleIds: ["client-architecture"]);
        var clientArchitecture = new TopologyNodeViewModel(
            "client-architecture",
            "client-architecture-id",
            "Architecture",
            "Technical",
            "Module",
            "engineering",
            "Engineering",
            0,
            "Nested child module",
            2,
            ParentModuleId: "client-module-id");
        var uiModule = new TopologyNodeViewModel(
            "ui-module",
            "ui-module-id",
            "UI",
            "Technical",
            "Module",
            "art",
            "Art",
            0,
            "Top-level art module",
            1);

        var nodes = new[]
        {
            project,
            engineering,
            art,
            rootTeam,
            clientModule,
            clientArchitecture,
            uiModule
        };

        var edges = new[]
        {
            new TopologyEdgeViewModel("project", "__dept__:engineering", "containment", false, "composition"),
            new TopologyEdgeViewModel("project", "__dept__:art", "containment", false, "composition"),
            new TopologyEdgeViewModel("project", "team-root", "containment", false, "composition"),
            new TopologyEdgeViewModel("__dept__:engineering", "client-module", "containment", false, "composition"),
            new TopologyEdgeViewModel("__dept__:art", "ui-module", "containment", false, "composition"),
            new TopologyEdgeViewModel("client-module", "client-architecture", "containment", false, "composition"),
            new TopologyEdgeViewModel("client-module", "ui-module", "dependency", false),
            new TopologyEdgeViewModel("client-architecture", "ui-module", "collaboration", false)
        };

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
        Assert.Equal(["__dept__:engineering", "client-module"], control.VisibleNodeIds);

        Assert.True(control.NavigateInto("client-module"));
        Assert.Equal("client-module", control.ViewRootId);
        Assert.Equal(["client-module", "client-architecture"], control.VisibleNodeIds);

        Assert.True(control.NavigateUp());
        Assert.Equal("__dept__:engineering", control.ViewRootId);
        Assert.Equal(["__dept__:engineering", "client-module"], control.VisibleNodeIds);
    }
}
