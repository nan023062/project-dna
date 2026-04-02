using Dna.Core.Config;
using Dna.Knowledge;
using Dna.Knowledge.Governance;
using Dna.Knowledge.Workspace.Models;
using Dna.Memory.Models;
using Dna.Memory.Store;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace App.Tests;

public sealed class GovernanceRefactorTests
{
    [Fact]
    public void CheckFreshness_ShouldNotRequireLegacyTopologySnapshot()
    {
        using var harness = GovernanceTestHarness.Create();

        harness.MemoryStore.Insert(new MemoryEntry
        {
            Id = "mem-fresh-1",
            Type = MemoryType.Working,
            NodeType = NodeType.Technical,
            Source = MemorySource.System,
            Content = "stale candidate",
            Summary = "stale candidate",
            NodeId = "tech-core",
            Freshness = FreshnessStatus.Fresh,
            Stage = MemoryStage.ShortTerm,
            CreatedAt = DateTime.UtcNow.AddDays(-10),
            StaleAfter = DateTime.UtcNow.AddDays(-1)
        });

        var decayed = harness.Engine.CheckFreshness();

        Assert.Equal(1, decayed);
        Assert.Equal(FreshnessStatus.Aging, harness.MemoryStore.GetById("mem-fresh-1")?.Freshness);
        Assert.False(harness.GraphEngine.TopologyRequested);
    }

    [Fact]
    public void DetectMemoryConflicts_ShouldNotRequireLegacyTopologySnapshot()
    {
        using var harness = GovernanceTestHarness.Create();

        harness.MemoryStore.Insert(new MemoryEntry
        {
            Id = "mem-id-1",
            Type = MemoryType.Structural,
            NodeType = NodeType.Technical,
            Source = MemorySource.System,
            Content = "{\"summary\":\"older\"}",
            Summary = "older",
            NodeId = "tech-core",
            Tags = [WellKnownTags.Identity],
            Freshness = FreshnessStatus.Fresh,
            Stage = MemoryStage.LongTerm,
            CreatedAt = DateTime.UtcNow.AddDays(-2)
        });
        harness.MemoryStore.Insert(new MemoryEntry
        {
            Id = "mem-id-2",
            Type = MemoryType.Structural,
            NodeType = NodeType.Technical,
            Source = MemorySource.System,
            Content = "{\"summary\":\"newer\"}",
            Summary = "newer",
            NodeId = "tech-core",
            Tags = [WellKnownTags.Identity],
            Freshness = FreshnessStatus.Fresh,
            Stage = MemoryStage.LongTerm,
            CreatedAt = DateTime.UtcNow.AddDays(-1)
        });

        var conflicts = harness.Engine.DetectMemoryConflicts();

        Assert.Equal(1, conflicts);
        Assert.Contains("#conflict", harness.MemoryStore.GetById("mem-id-1")?.Tags ?? []);
        Assert.False(harness.GraphEngine.TopologyRequested);
    }

    [Fact]
    public async Task CondenseNodeKnowledge_ShouldResolveNodeFromTopoGraphStoreWithoutGraphTopology()
    {
        using var harness = GovernanceTestHarness.Create();

        harness.MemoryStore.Insert(new MemoryEntry
        {
            Id = "mem-lesson-1",
            Type = MemoryType.Episodic,
            NodeType = NodeType.Technical,
            Source = MemorySource.System,
            Content = "{\"title\":\"Avoid direct SQL\",\"context\":\"Use store abstractions\",\"resolution\":\"Move SQL into store\"}",
            Summary = "Avoid direct SQL",
            NodeId = "tech-core",
            Tags = [WellKnownTags.Lesson],
            Freshness = FreshnessStatus.Fresh,
            Stage = MemoryStage.LongTerm,
            Importance = 0.8,
            CreatedAt = DateTime.UtcNow.AddDays(-2)
        });
        harness.MemoryStore.Insert(new MemoryEntry
        {
            Id = "mem-task-1",
            Type = MemoryType.Working,
            NodeType = NodeType.Technical,
            Source = MemorySource.System,
            Content = "{\"task\":\"Split topo store\",\"status\":\"doing\"}",
            Summary = "Split topo store",
            NodeId = "tech-core",
            Tags = [WellKnownTags.ActiveTask],
            Freshness = FreshnessStatus.Fresh,
            Stage = MemoryStage.ShortTerm,
            Importance = 0.7,
            CreatedAt = DateTime.UtcNow.AddDays(-1)
        });

        var result = await harness.Engine.CondenseNodeKnowledgeAsync("Core", 50);

        Assert.Equal("tech-core", result.NodeId);
        Assert.Equal("Core", result.NodeName);
        Assert.NotNull(result.NewIdentityMemoryId);
        Assert.True(harness.TopoStore.KnowledgeByNodeId.ContainsKey("tech-core"));
        Assert.False(harness.GraphEngine.TopologyRequested);
    }

    private sealed class GovernanceTestHarness : IDisposable
    {
        private readonly string _storePath;
        private readonly ServiceProvider _services;

        public MemoryStore MemoryStore { get; }
        public FakeTopoGraphStore TopoStore { get; }
        public ThrowingGraphEngine GraphEngine { get; }
        public GovernanceEngine Engine { get; }

        private GovernanceTestHarness(
            string storePath,
            ServiceProvider services,
            MemoryStore memoryStore,
            FakeTopoGraphStore topoStore,
            ThrowingGraphEngine graphEngine,
            GovernanceEngine engine)
        {
            _storePath = storePath;
            _services = services;
            MemoryStore = memoryStore;
            TopoStore = topoStore;
            GraphEngine = graphEngine;
            Engine = engine;
        }

        public static GovernanceTestHarness Create()
        {
            var services = new ServiceCollection()
                .AddLogging()
                .AddHttpClient()
                .BuildServiceProvider();

            var topoStore = new FakeTopoGraphStore(
                new ModulesManifest
                {
                    Disciplines = new Dictionary<string, List<ModuleRegistration>>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["engineering"] =
                        [
                            new ModuleRegistration
                            {
                                Id = "tech-core",
                                Name = "Core",
                                Path = "src/core",
                                Layer = 1,
                                Summary = "Core module"
                            }
                        ]
                    }
                },
                new ComputedManifest());

            var storePath = Path.Combine(Path.GetTempPath(), "dna-governance-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(storePath);

            var memoryStore = new MemoryStore(services);
            memoryStore.Initialize(storePath);
            memoryStore.BuildInternals(
                services.GetRequiredService<IHttpClientFactory>(),
                new ProjectConfig(),
                services.GetRequiredService<ILoggerFactory>(),
                topoStore);

            var graphEngine = new ThrowingGraphEngine();
            var governance = new GovernanceEngine(memoryStore, topoStore, graphEngine, NullLoggerFactory.Instance);

            return new GovernanceTestHarness(storePath, services, memoryStore, topoStore, graphEngine, governance);
        }

        public void Dispose()
        {
            MemoryStore.Dispose();
            _services.Dispose();
            SqliteConnection.ClearAllPools();

            if (Directory.Exists(_storePath))
                DeleteStorePathWithRetry(_storePath);
        }

        private static void DeleteStorePathWithRetry(string storePath)
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    Directory.Delete(storePath, recursive: true);
                    return;
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

            // Best effort cleanup for temp SQLite files on Windows; a delayed handle
            // release should not make the behavioral regression test fail.
        }
    }

    private sealed class FakeTopoGraphStore : ITopoGraphStore
    {
        private readonly ArchitectureManifest _architecture = new();

        public FakeTopoGraphStore(ModulesManifest manifest, ComputedManifest computed)
        {
            Manifest = manifest;
            Computed = computed;
        }

        public ModulesManifest Manifest { get; private set; }
        public ComputedManifest Computed { get; private set; }
        public Dictionary<string, NodeKnowledge> KnowledgeByNodeId { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void Initialize(string storePath)
        {
        }

        public void Reload()
        {
        }

        public ArchitectureManifest GetArchitecture() => _architecture;

        public ModulesManifest GetModulesManifest() => Manifest;

        public ComputedManifest GetComputedManifest() => Computed;

        public Dictionary<string, NodeKnowledge> LoadNodeKnowledgeMap() => KnowledgeByNodeId;

        public void UpsertNodeKnowledge(string nodeId, NodeKnowledge knowledge)
        {
            KnowledgeByNodeId[nodeId] = knowledge;
        }

        public List<string> ResolveNodeIdCandidates(string? nodeId, bool strict = false)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
                return [];

            var input = nodeId.Trim();
            foreach (var modules in Manifest.Disciplines.Values)
            {
                var match = modules.FirstOrDefault(module =>
                    string.Equals(module.Id, input, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(module.Name, input, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                    return [match.Id, match.Name];
            }

            if (strict)
                throw new InvalidOperationException($"missing node: {input}");

            return [input];
        }

        public void UpdateComputedDependencies(string moduleName, List<string> computedDependencies)
        {
            Computed.ModuleDependencies[moduleName] = computedDependencies;
        }

        public void RegisterModule(string discipline, ModuleRegistration module)
            => throw new NotSupportedException();

        public bool UnregisterModule(string name)
            => throw new NotSupportedException();

        public void SaveCrossWork(CrossWorkRegistration crossWork)
            => throw new NotSupportedException();

        public bool RemoveCrossWork(string crossWorkId)
            => throw new NotSupportedException();

        public void UpsertDiscipline(string disciplineId, string? displayName, string roleId, List<LayerDefinition> layers)
            => throw new NotSupportedException();

        public bool RemoveDiscipline(string disciplineId)
            => throw new NotSupportedException();

        public void ReplaceModulesManifest(ModulesManifest manifest)
        {
            Manifest = manifest;
        }
    }

    private sealed class ThrowingGraphEngine : IGraphEngine
    {
        public bool TopologyRequested { get; private set; }

        public TopologySnapshot BuildTopology()
        {
            TopologyRequested = true;
            throw new NotSupportedException("legacy topology should not be requested");
        }

        public TopologySnapshot GetTopology()
        {
            TopologyRequested = true;
            throw new NotSupportedException("legacy topology should not be requested");
        }

        public ExecutionPlan GetExecutionPlan(List<string> moduleNames) => throw new NotSupportedException();
        public KnowledgeNode? FindModule(string nameOrPath) => throw new NotSupportedException();
        public List<KnowledgeNode> GetAllModules() => throw new NotSupportedException();
        public List<KnowledgeNode> GetModulesByDiscipline(string disciplineId) => throw new NotSupportedException();
        public ModuleContext GetModuleContext(string targetModule, string? currentModule, List<string>? activeModules = null) => throw new NotSupportedException();
        public GovernanceReport ValidateArchitecture() => new();
        public List<CrossWork> GetCrossWorks() => throw new NotSupportedException();
        public List<CrossWork> GetCrossWorksForModule(string moduleName) => throw new NotSupportedException();
        public void RegisterModule(string discipline, ModuleRegistration module) => throw new NotSupportedException();
        public bool UnregisterModule(string name) => throw new NotSupportedException();
        public void SaveCrossWork(CrossWorkRegistration crossWork) => throw new NotSupportedException();
        public bool RemoveCrossWork(string crossWorkId) => throw new NotSupportedException();
        public void UpsertDiscipline(string disciplineId, string? displayName, string roleId, List<LayerDefinition> layers) => throw new NotSupportedException();
        public bool RemoveDiscipline(string disciplineId) => throw new NotSupportedException();
        public string? GetDisciplineRoleId(string moduleName) => throw new NotSupportedException();
        public ArchitectureManifest GetArchitecture() => throw new NotSupportedException();
        public ModulesManifest GetModulesManifest() => throw new NotSupportedException();
        public void ReplaceModulesManifest(ModulesManifest manifest) => throw new NotSupportedException();
        public void ReloadManifests() => throw new NotSupportedException();
        public void Initialize(string storePath) => throw new NotSupportedException();
    }
}
