using Dna.App.Services;
using Dna.Core.Config;
using Dna.Knowledge;
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
}
