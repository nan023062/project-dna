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
    public void BuildTopology_ShouldProduceTypedRelations_AndKeepDependencyEdges()
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
    }

    [Fact]
    public void BuildTopology_ShouldPopulateModuleContract_AndPathBinding()
    {
        SeedBaseKnowledgeSpace();
        var service = CreateService();

        service.Initialize(Path.Combine(_agenticOsPath, "knowledge"));
        service.RegisterModule("engineering", new TopologyModuleDefinition
        {
            Id = "AgenticOs/Program/AppFramework",
            Name = "AppFramework",
            Path = "src/app/framework",
            Layer = 1,
            Boundary = "open",
            PublicApi = ["IWindowHost", "IWorkspaceShell"],
            Constraints = ["No direct business logic"],
            ManagedPaths = ["src/app/shared", "src/app/contracts"],
            Summary = "Framework runtime"
        });

        var topology = service.BuildTopology();
        var node = Assert.Single(topology.Nodes);

        Assert.Equal("src/app/framework", node.PathBinding.MainPath);
        Assert.Contains("src/app/shared", node.PathBinding.ManagedPaths);
        Assert.Equal("open", node.ContractInfo.Boundary);
        Assert.Contains("IWindowHost", node.ContractInfo.PublicApi);
        Assert.Contains("No direct business logic", node.ContractInfo.Constraints);
        Assert.Equal("src/app/framework", node.RelativePath);
        Assert.Equal("open", node.Boundary);
        Assert.Contains("IWorkspaceShell", node.PublicApi ?? []);
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
