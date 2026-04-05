using Dna.Knowledge;
using Dna.Knowledge.FileProtocol;
using Dna.Knowledge.FileProtocol.Models;
using Dna.Knowledge.TopoGraph;
using Dna.Knowledge.TopoGraph.Internal.Builders;
using Dna.Knowledge.TopoGraph.Models.Nodes;
using Dna.Knowledge.Workspace.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace App.Tests;

public sealed class TopoGraphApplicationServiceTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"dna-topograph-{Guid.NewGuid():N}");
    private readonly string _agenticOsPath;
    private readonly ServiceProvider _services;

    public TopoGraphApplicationServiceTests()
    {
        _agenticOsPath = Path.Combine(_tempRoot, ".agentic-os");
        Directory.CreateDirectory(_agenticOsPath);
        _services = new ServiceCollection()
            .AddLogging()
            .AddHttpClient()
            .BuildServiceProvider();
    }

    public void Dispose()
    {
        _services.Dispose();
        SqliteConnection.ClearAllPools();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(_tempRoot))
                    Directory.Delete(_tempRoot, recursive: true);
                break;
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(100);
            }
            catch (UnauthorizedAccessException) when (attempt < 4)
            {
                Thread.Sleep(100);
            }
        }
    }

    [Fact]
    public void BuildTopology_ShouldProduceTypedRelations_AndModuleRuntimeMetadata()
    {
        SeedBaseKnowledgeSpace();
        var service = CreateService();

        service.Initialize(Path.Combine(_agenticOsPath, "knowledge"));
        service.RegisterModule("engineering", new TopologyModuleDefinition
        {
            Id = "AgenticOs/Program/Core",
            Name = "Core",
            Path = "src/core",
            Layer = 1,
            Summary = "Core module",
            Boundary = "semi-open",
            PublicApi = ["ICoreApi"],
            Constraints = ["No UI"],
            ManagedPaths = ["src/shared"]
        });
        service.RegisterModule("engineering", new TopologyModuleDefinition
        {
            Id = "AgenticOs/Program/Combat",
            Name = "Combat",
            Path = "src/combat",
            Layer = 2,
            ParentModuleId = "Core",
            Dependencies = ["Core"],
            Summary = "Combat feature"
        });
        service.SaveCrossWork(new TopologyCrossWorkDefinition
        {
            Id = "AgenticOs/Program/LiveOps",
            Name = "LiveOps",
            Description = "Cross module liveops work",
            Participants =
            [
                new TopologyCrossWorkParticipantDefinition
                {
                    ModuleName = "Combat",
                    Role = "owner",
                    Deliverable = "Patch plan"
                },
                new TopologyCrossWorkParticipantDefinition
                {
                    ModuleName = "Core",
                    Role = "support",
                    Contract = "Provide runtime hooks"
                }
            ]
        });

        var topology = service.BuildTopology();

        Assert.Contains(topology.Relations, relation =>
            relation.Type == TopologyRelationType.Dependency &&
            relation.FromId == "AgenticOs/Program/Combat" &&
            relation.ToId == "AgenticOs/Program/Core" &&
            !relation.IsComputed);

        Assert.Contains(topology.Relations, relation =>
            relation.Type == TopologyRelationType.Collaboration &&
            relation.IsComputed);

        Assert.Contains(topology.Edges, edge =>
            edge.From == "Combat" &&
            edge.To == "Core" &&
            !edge.IsComputed);

        Assert.Contains(topology.DepMap, pair =>
            pair.Key == "Combat" &&
            pair.Value.Contains("Core", StringComparer.OrdinalIgnoreCase));

        var coreNode = Assert.Single(topology.Nodes, node => node.Name == "Core");
        Assert.Equal("src/core", coreNode.PathBinding.MainPath);
        Assert.Contains("src/shared", coreNode.PathBinding.ManagedPaths);
        Assert.Equal("semi-open", coreNode.ContractInfo.Boundary);
        Assert.Contains("ICoreApi", coreNode.ContractInfo.PublicApi);
        Assert.Contains("No UI", coreNode.ContractInfo.Constraints);
        Assert.Equal("src/core", coreNode.RelativePath);
        Assert.Equal("semi-open", coreNode.Boundary);
        Assert.Contains("ICoreApi", coreNode.PublicApi ?? []);
    }

    [Fact]
    public void ModuleKnowledgeFacade_ShouldExposeStableKnowledgeAndRelationViews()
    {
        SeedBaseKnowledgeSpace();
        var service = CreateService();

        service.Initialize(Path.Combine(_agenticOsPath, "knowledge"));
        service.RegisterModule("engineering", new TopologyModuleDefinition
        {
            Id = "AgenticOs/Program/Core",
            Name = "Core",
            Path = "src/core",
            Layer = 1,
            Summary = "Core module",
            Boundary = "semi-open",
            PublicApi = ["ICoreApi"],
            Constraints = ["No UI"]
        });
        service.RegisterModule("engineering", new TopologyModuleDefinition
        {
            Id = "AgenticOs/Program/Combat",
            Name = "Combat",
            Path = "src/combat",
            Layer = 2,
            ParentModuleId = "Core",
            Dependencies = ["Core"],
            Summary = "Combat feature"
        });
        service.SaveCrossWork(new TopologyCrossWorkDefinition
        {
            Id = "AgenticOs/Program/CombatFlow",
            Name = "CombatFlow",
            Description = "Combat delivery flow",
            Participants =
            [
                new TopologyCrossWorkParticipantDefinition
                {
                    ModuleName = "Combat",
                    Role = "owner",
                    Deliverable = "Combat patch"
                },
                new TopologyCrossWorkParticipantDefinition
                {
                    ModuleName = "Core",
                    Role = "support",
                    Contract = "Provide runtime contract"
                }
            ]
        });

        var saved = service.SaveModuleKnowledge(new TopologyModuleKnowledgeUpsertCommand
        {
            NodeIdOrName = "Combat",
            Knowledge = new NodeKnowledge
            {
                Identity = "Combat module identity.",
                Lessons =
                [
                    new LessonSummary
                    {
                        Title = "Decouple UI bindings",
                        Resolution = "Route through gameplay ports"
                    }
                ],
                ActiveTasks = ["Split combat orchestration"],
                Facts = ["Uses Core runtime services"],
                TotalMemoryCount = 2,
                IdentityMemoryId = "mem-combat-identity",
                UpgradeTrailMemoryId = "mem-combat-trail",
                MemoryIds = ["mem-a", "mem-b"]
            }
        });

        Assert.Equal("AgenticOs/Program/Combat", saved.NodeId);
        Assert.Equal("Combat module identity.", saved.Knowledge.Identity);
        Assert.Equal(["Split combat orchestration"], saved.Knowledge.ActiveTasks);
        Assert.Equal(["Uses Core runtime services"], saved.Knowledge.Facts);

        var listed = service.ListModuleKnowledge();
        Assert.Contains(listed, item =>
            item.NodeId == "AgenticOs/Program/Combat" &&
            item.Knowledge.Identity == "Combat module identity.");

        var fetched = service.GetModuleKnowledge("AgenticOs/Program/Combat");
        Assert.NotNull(fetched);
        Assert.Equal("mem-combat-identity", fetched!.Knowledge.IdentityMemoryId);
        Assert.Equal("semi-open", service.GetModuleKnowledge("Core")!.Boundary);

        var relations = service.GetModuleRelations("Combat");
        Assert.NotNull(relations);
        Assert.Contains(relations!.Outgoing, relation =>
            relation.Type == TopologyRelationType.Dependency &&
            relation.ToId == "AgenticOs/Program/Core");
        var allRelations = relations.Outgoing.Concat(relations.Incoming).ToList();
        Assert.Contains(allRelations, relation =>
            relation.Type == TopologyRelationType.Collaboration &&
            ((relation.FromId == "AgenticOs/Program/Combat" && relation.ToId == "AgenticOs/Program/Core") ||
             (relation.FromId == "AgenticOs/Program/Core" && relation.ToId == "AgenticOs/Program/Combat")));
        Assert.Contains(relations.Incoming, relation =>
            relation.Type == TopologyRelationType.Containment);
    }

    [Fact]
    public void WorkbenchSnapshot_ShouldExposeProjectDisciplineAndModuleRelationViews()
    {
        SeedBaseKnowledgeSpace();
        var service = CreateService();

        service.Initialize(Path.Combine(_agenticOsPath, "knowledge"));
        service.RegisterModule("engineering", new TopologyModuleDefinition
        {
            Id = "AgenticOs/Program/Core",
            Name = "Core",
            Path = "src/core",
            Layer = 1,
            Summary = "Core module",
            Boundary = "semi-open",
            PublicApi = ["ICoreApi"],
            Constraints = ["No UI"]
        });
        service.RegisterModule("engineering", new TopologyModuleDefinition
        {
            Id = "AgenticOs/Program/Combat",
            Name = "Combat",
            Path = "src/combat",
            Layer = 2,
            ParentModuleId = "Core",
            Dependencies = ["Core"],
            Summary = "Combat feature"
        });
        service.SaveCrossWork(new TopologyCrossWorkDefinition
        {
            Id = "AgenticOs/Program/CombatFlow",
            Name = "CombatFlow",
            Description = "Combat delivery flow",
            Participants =
            [
                new TopologyCrossWorkParticipantDefinition
                {
                    ModuleName = "Combat",
                    Role = "owner",
                    Deliverable = "Combat patch"
                },
                new TopologyCrossWorkParticipantDefinition
                {
                    ModuleName = "Core",
                    Role = "support",
                    Contract = "Provide runtime contract"
                }
            ]
        });

        var workbench = service.GetWorkbenchSnapshot();

        Assert.Equal("project", workbench.Project.Id);
        Assert.Equal("Agentic OS", workbench.Project.Name);

        var discipline = Assert.Single(workbench.Disciplines);
        Assert.Equal("engineering", discipline.Id);
        Assert.Equal("Program", discipline.DisplayName);

        var core = Assert.Single(workbench.Modules, module => module.NodeId == "AgenticOs/Program/Core");
        Assert.Equal("Technical", core.TypeName);
        Assert.Equal("Program", core.DisciplineDisplayName);
        Assert.Equal("govern", core.FileAuthority);
        Assert.Equal("semi-open", core.Boundary);
        Assert.Equal(0, core.StructureDepth);
        Assert.Equal(10, core.ArchitectureLayerScore);

        var combat = Assert.Single(workbench.Modules, module => module.NodeId == "AgenticOs/Program/Combat");
        Assert.Equal("Combat", combat.Name);
        Assert.Equal("AgenticOs/Program/Core", combat.ParentModuleId);
        Assert.Contains("Core", combat.Dependencies);
        Assert.Equal(1, combat.StructureDepth);
        Assert.Equal(90, combat.ArchitectureLayerScore);

        Assert.Contains(workbench.ContainmentEdges, edge =>
            edge.From == "project" &&
            edge.To == "__dept__:engineering" &&
            edge.Relation == "containment");
        Assert.Contains(workbench.ContainmentEdges, edge =>
            edge.From == "__dept__:engineering" &&
            edge.To == "Core" &&
            edge.Relation == "containment");
        Assert.Contains(workbench.RelationEdges, edge =>
            edge.From == "Combat" &&
            edge.To == "Core" &&
            edge.Relation == "dependency");
        Assert.Contains(workbench.CollaborationEdges, edge =>
            edge.Relation == "collaboration" &&
            ((edge.From == "Combat" && edge.To == "Core") ||
             (edge.From == "Core" && edge.To == "Combat")));

        var mcdp = service.GetMcdpProjection("/tmp/agentic-os");
        Assert.Equal("1.0", mcdp.ProtocolVersion);
        Assert.Equal("/tmp/agentic-os", mcdp.ProjectRoot);
        Assert.Equal("Agentic OS", mcdp.ProjectName);

        var mcdpCore = Assert.Single(mcdp.Modules, module => module.Uid == "AgenticOs/Program/Core");
        Assert.Equal("Technical", mcdpCore.Type);
        Assert.Equal(10, mcdpCore.LayerScore);
        Assert.Null(mcdpCore.Relationships.Parent);
        Assert.Contains(mcdpCore.Relationships.Children, child => child == "AgenticOs/Program/Combat");

        var mcdpCombat = Assert.Single(mcdp.Modules, module => module.Uid == "AgenticOs/Program/Combat");
        Assert.Equal(90, mcdpCombat.LayerScore);
        Assert.Equal("AgenticOs/Program/Core", mcdpCombat.Relationships.Parent);
        Assert.Contains(mcdpCombat.Relationships.Dependencies, dependency =>
            dependency.Target == "AgenticOs/Program/Core");
    }

    private TopoGraphApplicationService CreateService()
    {
        var topoStore = new TopoGraphStore(_services);
        var facade = new TopoGraphFacade(new FileBasedDefinitionStore(), new TopologyModelBuilder());
        return new TopoGraphApplicationService(
            topoStore,
            facade,
            contextProvider: null,
            _services.GetRequiredService<ILoggerFactory>().CreateLogger<TopoGraphApplicationService>());
    }

    private void SeedBaseKnowledgeSpace()
    {
        var fileStore = new KnowledgeFileStore();

        fileStore.SaveModule(_agenticOsPath, new ModuleFile
        {
            Uid = "AgenticOs",
            Name = "Agentic OS",
            Type = TopologyNodeKind.Project
        }, "## Summary\n\nProject.\n");

        fileStore.SaveModule(_agenticOsPath, new ModuleFile
        {
            Uid = "AgenticOs/Program",
            Name = "Program",
            Type = TopologyNodeKind.Department,
            Parent = "AgenticOs",
            DisciplineCode = "engineering",
            RoleId = "coder",
            Layers = [new LayerDefinition { Level = 1, Name = "system" }, new LayerDefinition { Level = 2, Name = "feature" }]
        }, "## Summary\n\nEngineering department.\n");
    }
}
