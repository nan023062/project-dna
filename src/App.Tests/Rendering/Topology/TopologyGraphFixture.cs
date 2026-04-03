using Dna.App.Desktop;

namespace App.Tests;

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
        var appModule = new TopologyNodeViewModel(
            "app-module",
            "app-module-id",
            "App",
            "Technical",
            "Module",
            "engineering",
            "Engineering",
            0,
            "Top-level app module",
            1,
            ParentModuleId: null,
            ChildModuleIds: ["app-architecture"]);
        var appArchitecture = new TopologyNodeViewModel(
            "app-architecture",
            "app-architecture-id",
            "Architecture",
            "Technical",
            "Module",
            "engineering",
            "Engineering",
            0,
            "Nested child module",
            2,
            ParentModuleId: "app-module-id");
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
            appModule,
            appArchitecture,
            uiModule
        };

        var edges = new[]
        {
            new TopologyEdgeViewModel("project", "__dept__:engineering", "containment", false, "composition"),
            new TopologyEdgeViewModel("project", "__dept__:art", "containment", false, "composition"),
            new TopologyEdgeViewModel("project", "team-root", "containment", false, "composition"),
            new TopologyEdgeViewModel("__dept__:engineering", "app-module", "containment", false, "composition"),
            new TopologyEdgeViewModel("__dept__:art", "ui-module", "containment", false, "composition"),
            new TopologyEdgeViewModel("app-module", "app-architecture", "containment", false, "composition"),
            new TopologyEdgeViewModel("app-module", "ui-module", "dependency", false),
            new TopologyEdgeViewModel("app-architecture", "ui-module", "collaboration", false)
        };

        return (nodes, edges);
    }
}
