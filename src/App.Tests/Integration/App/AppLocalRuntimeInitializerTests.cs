using Dna.App.Services;
using Dna.Core.Config;
using Dna.Knowledge;
using Dna.Knowledge.FileProtocol;
using Dna.Knowledge.FileProtocol.Models;
using Dna.Knowledge.TopoGraph;
using Dna.Knowledge.TopoGraph.Contracts;
using Dna.Knowledge.TopoGraph.Models.Nodes;
using Dna.Memory.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace App.Tests;

public sealed class AppLocalRuntimeInitializerTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"dna-runtime-init-{Guid.NewGuid():N}");
    private readonly string _workspaceRoot;
    private readonly string _metadataRoot;
    private readonly ServiceProvider _services;

    public AppLocalRuntimeInitializerTests()
    {
        _workspaceRoot = Path.Combine(_tempRoot, "workspace");
        _metadataRoot = Path.Combine(_workspaceRoot, ".agentic-os");

        Directory.CreateDirectory(_workspaceRoot);
        Directory.CreateDirectory(_metadataRoot);

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        serviceCollection.AddHttpClient();
        serviceCollection.AddSingleton(new AppRuntimeOptions
        {
            ProjectName = "agentic-os-dev",
            WorkspaceRoot = _workspaceRoot,
            MetadataRootPath = _metadataRoot
        });
        serviceCollection.AddSingleton<ProjectConfig>();
        serviceCollection.AddKnowledgeGraph();

        _services = serviceCollection.BuildServiceProvider();
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
    public async Task StartAsync_ShouldMigrateLegacyMemoryDatabase_AndInitializeWorkspace()
    {
        var memoryRoot = Path.Combine(_metadataRoot, "memory");
        Directory.CreateDirectory(memoryRoot);

        var dbPath = Path.Combine(memoryRoot, "memory.db");
        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            connection.Open();

            using var create = connection.CreateCommand();
            create.CommandText = """
                CREATE TABLE memory_entries (
                    id              TEXT PRIMARY KEY,
                    type            TEXT NOT NULL,
                    layer           TEXT NOT NULL,
                    source          TEXT NOT NULL,
                    content         TEXT NOT NULL DEFAULT '',
                    summary         TEXT,
                    importance      REAL DEFAULT 0.5,
                    freshness       TEXT DEFAULT 'Fresh',
                    created_at      TEXT NOT NULL,
                    last_verified_at TEXT,
                    stale_after     TEXT,
                    superseded_by   TEXT,
                    parent_id       TEXT,
                    node_id         TEXT,
                    version         INTEGER DEFAULT 1,
                    file_path       TEXT,
                    embedding       BLOB,
                    ext_source_url  TEXT,
                    ext_source_id   TEXT
                );
                """;
            create.ExecuteNonQuery();
        }

        var initializer = ActivatorUtilities.CreateInstance<AppLocalRuntimeInitializer>(_services);
        var exception = await Record.ExceptionAsync(() => initializer.StartAsync(CancellationToken.None));
        Assert.Null(exception);

        using var verifyConnection = new SqliteConnection($"Data Source={dbPath}");
        verifyConnection.Open();

        using var columnQuery = verifyConnection.CreateCommand();
        columnQuery.CommandText = "PRAGMA table_info(memory_entries)";
        using var columnReader = columnQuery.ExecuteReader();
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (columnReader.Read())
            columns.Add(columnReader.GetString(1));

        Assert.Contains("stage", columns);
        Assert.True(Directory.Exists(Path.Combine(_metadataRoot, "knowledge")));
    }

    [Fact]
    public async Task StartAsync_ShouldLoadSessionFilesAsShortTermMemory()
    {
        var sessionStore = new SessionFileStore();
        sessionStore.SaveSession(_metadataRoot, new SessionFile
        {
            Id = "01JTESTSESSIONINIT000000000",
            Type = "Working",
            Source = "Ai",
            NodeId = "AgenticOs/Program/App",
            Tags = [WellKnownTags.ActiveTask],
            CreatedAt = new DateTime(2026, 4, 3, 12, 0, 0, DateTimeKind.Utc),
            Body = "{\"task\":\"load session into runtime\"}",
            Category = FileProtocolPaths.TasksDir
        });

        var initializer = ActivatorUtilities.CreateInstance<AppLocalRuntimeInitializer>(_services);
        var exception = await Record.ExceptionAsync(() => initializer.StartAsync(CancellationToken.None));
        Assert.Null(exception);

        var memory = _services.GetRequiredService<IMemoryEngine>();
        var sessions = memory.QueryMemories(new MemoryFilter
        {
            Stages = [MemoryStage.ShortTerm],
            Limit = 20
        });

        var loaded = Assert.Single(sessions, item => item.Id == "01JTESTSESSIONINIT000000000");
        Assert.Equal(MemoryType.Working, loaded.Type);
        Assert.Equal(MemoryStage.ShortTerm, loaded.Stage);
        Assert.Equal("AgenticOs/Program/App", loaded.NodeId);
        Assert.Contains(WellKnownTags.ActiveTask, loaded.Tags);
    }

    [Fact]
    public async Task StartAsync_ShouldLoadMinimalKnowledgeMemoryAndSessionChain()
    {
        var knowledgeStore = new KnowledgeFileStore();
        knowledgeStore.SaveModule(_metadataRoot, new ModuleFile
        {
            Uid = "AgenticOs",
            Name = "Agentic OS",
            Type = TopologyNodeKind.Project
        }, "## Summary\n\nProject.\n");
        knowledgeStore.SaveModule(_metadataRoot, new ModuleFile
        {
            Uid = "AgenticOs/Program",
            Name = "Program",
            Type = TopologyNodeKind.Department,
            Parent = "AgenticOs",
            DisciplineCode = "engineering",
            RoleId = "coder",
            Layers = [new LayerDefinition { Level = 1, Name = "system" }, new LayerDefinition { Level = 2, Name = "feature" }]
        }, "## Summary\n\nEngineering department.\n");
        knowledgeStore.SaveModule(_metadataRoot, new ModuleFile
        {
            Uid = "AgenticOs/Program/App",
            Name = "App",
            Type = TopologyNodeKind.Team,
            Parent = "AgenticOs/Program",
            Keywords = ["desktop", "runtime"]
        }, "## Summary\n\nDesktop host.\n", [
            new DependencyEntry
            {
                Target = "AgenticOs/Program/DnaKnowledge",
                Type = "Association",
                Note = "Consume knowledge services"
            }
        ]);

        var memoryStore = new MemoryFileStore();
        memoryStore.SaveMemory(_metadataRoot, new MemoryFile
        {
            Id = "01JTESTMEMCHAIN000000000000",
            Type = "Semantic",
            Source = "Human",
            NodeId = "AgenticOs/Program/App",
            Disciplines = ["engineering"],
            Tags = ["decision"],
            Importance = 0.8,
            CreatedAt = new DateTime(2026, 4, 3, 9, 0, 0, DateTimeKind.Utc),
            Body = "# Decision\n\nApp consumes the local runtime.",
            Category = "decisions"
        });

        var sessionStore = new SessionFileStore();
        sessionStore.SaveSession(_metadataRoot, new SessionFile
        {
            Id = "01JTESTSESSIONCHAIN00000000",
            Type = "Working",
            Source = "Ai",
            NodeId = "AgenticOs/Program/App",
            Tags = [WellKnownTags.ActiveTask],
            CreatedAt = new DateTime(2026, 4, 3, 10, 0, 0, DateTimeKind.Utc),
            Body = "{\"task\":\"verify minimal chain\"}",
            Category = FileProtocolPaths.TasksDir
        });

        var initializer = ActivatorUtilities.CreateInstance<AppLocalRuntimeInitializer>(_services);
        var exception = await Record.ExceptionAsync(() => initializer.StartAsync(CancellationToken.None));
        Assert.Null(exception);

        var facadeSnapshot = _services.GetRequiredService<ITopoGraphFacade>().GetSnapshot();
        Assert.Contains(facadeSnapshot.Nodes, node => node.Id == "AgenticOs");
        Assert.Contains(facadeSnapshot.Nodes, node => node.Id == "AgenticOs/Program");
        Assert.Contains(facadeSnapshot.Nodes, node => node.Id == "AgenticOs/Program/App");

        var memory = _services.GetRequiredService<IMemoryEngine>();
        var longTermEntries = memory.QueryMemories(new MemoryFilter
        {
            NodeId = "AgenticOs/Program/App",
            Stages = [MemoryStage.LongTerm],
            Limit = 20
        });
        var shortTermEntries = memory.QueryMemories(new MemoryFilter
        {
            NodeId = "AgenticOs/Program/App",
            Stages = [MemoryStage.ShortTerm],
            Limit = 20
        });

        Assert.Contains(longTermEntries, entry => entry.Id == "01JTESTMEMCHAIN000000000000");
        Assert.Contains(shortTermEntries, entry => entry.Id == "01JTESTSESSIONCHAIN00000000");
    }
}
