using Dna.Knowledge;
using Dna.Knowledge.FileProtocol;
using Dna.Knowledge.FileProtocol.Models;
using Dna.Knowledge.TopoGraph;
using Dna.Knowledge.TopoGraph.Internal.Builders;
using Dna.Knowledge.TopoGraph.Models.Nodes;
using Dna.Knowledge.Workspace.Models;
using Dna.Memory.Models;
using Dna.Memory.Store;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace App.Tests;

public sealed class FileProtocolSyncTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"dna-file-sync-{Guid.NewGuid():N}");
    private readonly string _agenticOsPath;
    private readonly ServiceProvider _services;

    public FileProtocolSyncTests()
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
    public void MemoryStore_ShouldRouteMemoryByStage_AndArchiveFiles()
    {
        var topoStore = new StubTopoGraphStore();
        using var memoryStore = new MemoryStore(_services);
        memoryStore.Initialize(Path.Combine(_agenticOsPath, "memory"));
        memoryStore.BuildInternals(
            _services.GetRequiredService<IHttpClientFactory>(),
            new Dna.Core.Config.ProjectConfig(),
            _services.GetRequiredService<ILoggerFactory>(),
            topoStore);

        memoryStore.Insert(new MemoryEntry
        {
            Id = "01JTESTMEM0000000000000000",
            Type = MemoryType.Structural,
            NodeType = NodeType.Technical,
            Source = MemorySource.System,
            Content = "memory body",
            Summary = "memory summary",
            NodeId = "AgenticOs/Program/App",
            Disciplines = ["engineering"],
            Tags = ["#identity"],
            Stage = MemoryStage.LongTerm,
            Freshness = FreshnessStatus.Fresh,
            CreatedAt = DateTime.UtcNow
        });

        memoryStore.Insert(new MemoryEntry
        {
            Id = "01JTESTSESSION0000000000000",
            Type = MemoryType.Working,
            NodeType = NodeType.Technical,
            Source = MemorySource.System,
            Content = "{\"task\":\"wire session\"}",
            Summary = "wire session",
            NodeId = "AgenticOs/Program/App",
            Tags = [WellKnownTags.ActiveTask],
            Stage = MemoryStage.ShortTerm,
            Freshness = FreshnessStatus.Fresh,
            CreatedAt = DateTime.UtcNow
        });

        var longTermPath = Path.Combine(_agenticOsPath, "memory", "decisions", "01JTESTMEM0000000000000000.md");
        var shortTermPath = Path.Combine(_agenticOsPath, "session", "tasks", "01JTESTSESSION0000000000000.md");

        Assert.True(File.Exists(longTermPath));
        Assert.True(File.Exists(shortTermPath));

        memoryStore.UpdateFreshness("01JTESTMEM0000000000000000", FreshnessStatus.Archived);
        memoryStore.UpdateFreshness("01JTESTSESSION0000000000000", FreshnessStatus.Archived);

        Assert.False(File.Exists(longTermPath));
        Assert.False(File.Exists(shortTermPath));
    }

    [Fact]
    public void MemoryStore_ShouldRebuildShortTermMemoryFromSessionFiles()
    {
        var sessionStore = new SessionFileStore();
        sessionStore.SaveSession(_agenticOsPath, new SessionFile
        {
            Id = "01JSESSIONREBUILD00000000000",
            Type = "Working",
            Source = "Ai",
            NodeId = "AgenticOs/Program/App",
            Tags = [WellKnownTags.ActiveTask],
            CreatedAt = new DateTime(2026, 4, 3, 12, 0, 0, DateTimeKind.Utc),
            Body = "{\"task\":\"rebuild from session\"}",
            Category = FileProtocolPaths.TasksDir
        });

        using var memoryStore = new MemoryStore(_services);
        memoryStore.Initialize(Path.Combine(_agenticOsPath, "memory"));

        var rebuilt = memoryStore.GetById("01JSESSIONREBUILD00000000000");
        Assert.NotNull(rebuilt);
        Assert.Equal(MemoryStage.ShortTerm, rebuilt!.Stage);
        Assert.Equal(MemoryType.Working, rebuilt.Type);
        Assert.Equal("AgenticOs/Program/App", rebuilt.NodeId);
        Assert.Contains(WellKnownTags.ActiveTask, rebuilt.Tags);
    }

    [Fact]
    public void TopoGraphFileProtocol_ShouldPersistModuleAndKnowledgeWithoutLegacyKnowledgeTable()
    {
        SeedKnowledgeSpace(includeTeamModule: false);

        var topoStore = new TopoGraphStore(_services);
        var facade = new TopoGraphFacade(new FileBasedDefinitionStore(), new TopologyModelBuilder());
        var service = new TopoGraphApplicationService(
            topoStore,
            facade,
            contextProvider: null,
            _services.GetRequiredService<ILoggerFactory>().CreateLogger<TopoGraphApplicationService>());

        service.Initialize(Path.Combine(_agenticOsPath, "knowledge"));
        service.RegisterModule("engineering", new TopologyModuleDefinition
        {
            Name = "App",
            Path = "src/app",
            Layer = 3,
            Summary = "App shell"
        });

        var topology = service.BuildTopology();
        var appNode = Assert.Single(topology.Nodes, node => node.Name == "App");
        Assert.Equal(NodeType.Team, appNode.Type);
        Assert.Equal("src/app", appNode.RelativePath);

        var moduleJsonPath = Path.Combine(_agenticOsPath, "knowledge", "modules", "AgenticOs", "Program", "App", "module.json");
        Assert.True(File.Exists(moduleJsonPath));

        var savedKnowledge = service.SaveModuleKnowledge(new TopologyModuleKnowledgeUpsertCommand
        {
            NodeIdOrName = "App",
            Knowledge = new NodeKnowledge
            {
                Identity = "App runtime entrypoint.",
                Lessons =
                [
                    new LessonSummary
                    {
                        Title = "Avoid direct routing",
                        Resolution = "Prefer facade-backed endpoints"
                    }
                ],
                ActiveTasks = ["Move API to new facade"],
                Facts = ["Reads .agentic-os directly"],
                TotalMemoryCount = 3,
                IdentityMemoryId = "mem-identity-1",
                UpgradeTrailMemoryId = "mem-trail-1",
                MemoryIds = ["mem-source-1", "mem-identity-1", "mem-trail-1"]
            }
        });

        Assert.Equal("AgenticOs/Program/App", savedKnowledge.NodeId);
        Assert.Equal("App runtime entrypoint.", savedKnowledge.Knowledge.Identity);

        var identityPath = Path.Combine(_agenticOsPath, "knowledge", "modules", "AgenticOs", "Program", "App", "identity.md");
        var content = File.ReadAllText(identityPath);
        Assert.Contains("## Summary", content);
        Assert.Contains("App runtime entrypoint.", content);
        Assert.Contains("## Lessons", content);
        Assert.Contains("Move API to new facade", content);
        Assert.Contains("## Governance", content);
        Assert.Contains("Identity Memory: `mem-identity-1`", content);
        Assert.Contains("Upgrade Trail: `mem-trail-1`", content);
        Assert.Contains("Source Count: 3", content);

        var reloadedStore = new TopoGraphStore(_services);
        reloadedStore.Initialize(Path.Combine(_agenticOsPath, "knowledge"));
        var knowledgeMap = reloadedStore.LoadNodeKnowledgeMap();
        var reloaded = Assert.Contains("AgenticOs/Program/App", knowledgeMap);

        Assert.Equal("App runtime entrypoint.", reloaded.Identity);
        var lesson = Assert.Single(reloaded.Lessons);
        Assert.Equal("Avoid direct routing", lesson.Title);
        Assert.Equal("Prefer facade-backed endpoints", lesson.Resolution);
        Assert.Equal(["Move API to new facade"], reloaded.ActiveTasks);
        Assert.Equal(["Reads .agentic-os directly"], reloaded.Facts);
        Assert.Equal(3, reloaded.TotalMemoryCount);
        Assert.Equal("mem-identity-1", reloaded.IdentityMemoryId);
        Assert.Equal("mem-trail-1", reloaded.UpgradeTrailMemoryId);
        Assert.Equal(["mem-source-1", "mem-identity-1", "mem-trail-1"], reloaded.MemoryIds);

        var graphDbPath = Path.Combine(_agenticOsPath, "knowledge", "graph.db");
        using var conn = new SqliteConnection($"Data Source={graphDbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'graph_node_knowledge'";
        var tableCount = Convert.ToInt32(cmd.ExecuteScalar());
        Assert.Equal(0, tableCount);
    }

    private void SeedKnowledgeSpace(bool includeTeamModule)
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
            Layers = [new LayerDefinition { Level = 1, Name = "system" }, new LayerDefinition { Level = 3, Name = "application" }]
        }, "## Summary\n\nEngineering department.\n");

        if (!includeTeamModule)
            return;

        fileStore.SaveModule(_agenticOsPath, new ModuleFile
        {
            Uid = "AgenticOs/Program/App",
            Name = "App",
            Type = TopologyNodeKind.Team,
            Parent = "AgenticOs/Program",
            MainPath = "src/app",
            Layer = 3,
            BusinessObjective = "Run local app"
        }, "## Summary\n\nApp module.\n");
    }

    private sealed class StubTopoGraphStore : ITopoGraphStore
    {
        public void Initialize(string storePath) { }
        public void Reload() { }
        public ComputedManifest GetComputedManifest() => new();
        public Dictionary<string, NodeKnowledge> LoadNodeKnowledgeMap() => new(StringComparer.OrdinalIgnoreCase);
        public void UpsertNodeKnowledge(string nodeId, NodeKnowledge knowledge) { }
        public List<string> ResolveNodeIdCandidates(string? nodeId, bool strict = false) => string.IsNullOrWhiteSpace(nodeId) ? [] : [nodeId.Trim()];
        public void UpdateComputedDependencies(string moduleName, List<string> computedDependencies) { }
    }
}