using Dna.Client.Desktop;

namespace Client.Tests;

internal static class TopologyGraphFixture
{
    public static (TopologyNodeViewModel[] Nodes, TopologyEdgeViewModel[] Edges) CreateHierarchyGraph()
    {
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

        return (nodes, edges);
    }
}
