using Dna.Core.Config;
using Dna.Knowledge;
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
        Assert.False(harness.TopologyService.TopologyRequested);
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
        Assert.False(harness.TopologyService.TopologyRequested);
    }

    [Fact]
    public async Task CondenseNodeKnowledge_ShouldResolveNodeFromTopoGraphManagementSnapshot()
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
        Assert.NotNull(result.UpgradeTrailMemoryId);
        Assert.Equal(1, result.SessionSourceCount);
        Assert.Equal(1, result.MemorySourceCount);
        Assert.Contains("mem-task-1", result.SessionSourceMemoryIds);
        Assert.Contains("mem-lesson-1", result.MemorySourceMemoryIds);
        Assert.Contains("mem-task-1", result.ArchivedMemoryIds);
        Assert.Equal(FreshnessStatus.Archived, harness.MemoryStore.GetById("mem-task-1")?.Freshness);
        Assert.Equal(MemoryStage.LongTerm, harness.MemoryStore.GetById(result.UpgradeTrailMemoryId!)?.Stage);
        Assert.True(harness.TopoStore.KnowledgeByNodeId.ContainsKey("tech-core"));
        Assert.Equal(result.NewIdentityMemoryId, harness.TopoStore.KnowledgeByNodeId["tech-core"].IdentityMemoryId);
        Assert.Equal(result.UpgradeTrailMemoryId, harness.TopoStore.KnowledgeByNodeId["tech-core"].UpgradeTrailMemoryId);
        Assert.False(harness.TopologyService.TopologyRequested);
    }

    [Fact]
    public async Task EvolveKnowledge_ShouldSuggestSessionToMemoryAndMemoryToKnowledge()
    {
        using var harness = GovernanceTestHarness.Create();

        harness.MemoryStore.Insert(new MemoryEntry
        {
            Id = "mem-session-1",
            Type = MemoryType.Working,
            NodeType = NodeType.Technical,
            Source = MemorySource.System,
            Content = "{\"task\":\"stabilize governance api\",\"status\":\"doing\"}",
            Summary = "stabilize governance api",
            NodeId = "tech-core",
            Tags = [WellKnownTags.ActiveTask],
            Freshness = FreshnessStatus.Fresh,
            Stage = MemoryStage.ShortTerm,
            Importance = 0.85,
            CreatedAt = DateTime.UtcNow.AddMinutes(-30)
        });

        harness.MemoryStore.Insert(new MemoryEntry
        {
            Id = "mem-memory-1",
            Type = MemoryType.Structural,
            NodeType = NodeType.Technical,
            Source = MemorySource.System,
            Content = "{\"summary\":\"Core module identity\"}",
            Summary = "Core module identity",
            NodeId = "tech-core",
            Tags = [WellKnownTags.Identity, "#decision"],
            Freshness = FreshnessStatus.Fresh,
            Stage = MemoryStage.LongTerm,
            Importance = 0.9,
            CreatedAt = DateTime.UtcNow.AddDays(-1)
        });

        var report = await harness.Engine.EvolveKnowledgeAsync("Core", 10);

        Assert.Equal("tech-core", report.FilterNodeId);
        Assert.Equal(1, report.SessionToMemoryCount);
        Assert.Equal(1, report.MemoryToKnowledgeCount);

        var sessionSuggestion = Assert.Single(report.Suggestions, item => item.MemoryId == "mem-session-1");
        Assert.Equal(EvolutionKnowledgeLayer.Session, sessionSuggestion.CurrentLayer);
        Assert.Equal(EvolutionKnowledgeLayer.Memory, sessionSuggestion.TargetLayer);
        Assert.Contains("tech-core", sessionSuggestion.CandidateModuleIds);

        var memorySuggestion = Assert.Single(report.Suggestions, item => item.MemoryId == "mem-memory-1");
        Assert.Equal(EvolutionKnowledgeLayer.Memory, memorySuggestion.CurrentLayer);
        Assert.Equal(EvolutionKnowledgeLayer.Knowledge, memorySuggestion.TargetLayer);
        Assert.Contains("Core", memorySuggestion.CandidateModuleNames);
    }

    [Fact]
    public void ValidateArchitecture_ShouldDelegateToTopologyApplicationService()
    {
        using var harness = GovernanceTestHarness.Create();

        var report = harness.Engine.ValidateArchitecture();

        Assert.True(harness.TopologyService.TopologyRequested);
        Assert.True(harness.TopologyService.ValidationRequested);
        Assert.NotNull(report);
    }

    private sealed class GovernanceTestHarness : IDisposable
    {
        private readonly string _storePath;
        private readonly ServiceProvider _services;

        public MemoryStore MemoryStore { get; }
        public FakeTopoGraphStore TopoStore { get; }
        public ThrowingTopologyApplicationService TopologyService { get; }
        public GovernanceEngine Engine { get; }

        private GovernanceTestHarness(
            string storePath,
            ServiceProvider services,
            MemoryStore memoryStore,
            FakeTopoGraphStore topoStore,
            ThrowingTopologyApplicationService topologyService,
            GovernanceEngine engine)
        {
            _storePath = storePath;
            _services = services;
            MemoryStore = memoryStore;
            TopoStore = topoStore;
            TopologyService = topologyService;
            Engine = engine;
        }

        public static GovernanceTestHarness Create()
        {
            var services = new ServiceCollection()
                .AddLogging()
                .AddHttpClient()
                .BuildServiceProvider();

            var modules =
                new List<TopologyModuleDefinition>
                {
                    new()
                    {
                        Id = "tech-core",
                        Name = "Core",
                        Discipline = "engineering",
                        Path = "src/core",
                        Layer = 1,
                        Summary = "Core module"
                    }
                };

            var topoStore = new FakeTopoGraphStore(modules, new ComputedManifest());

            var storePath = Path.Combine(Path.GetTempPath(), "dna-governance-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(storePath);

            var memoryStore = new MemoryStore(services);
            memoryStore.Initialize(storePath);
            memoryStore.BuildInternals(
                services.GetRequiredService<IHttpClientFactory>(),
                new ProjectConfig(),
                services.GetRequiredService<ILoggerFactory>(),
                topoStore);

            var topologyService = new ThrowingTopologyApplicationService(modules);
            var governance = new GovernanceEngine(memoryStore, topoStore, topologyService, NullLoggerFactory.Instance);

            return new GovernanceTestHarness(storePath, services, memoryStore, topoStore, topologyService, governance);
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
        }
    }

    private sealed class FakeTopoGraphStore : ITopoGraphStore
    {
        private readonly List<TopologyModuleDefinition> _modules;

        public FakeTopoGraphStore(List<TopologyModuleDefinition> modules, ComputedManifest computed)
        {
            _modules = modules;
            Computed = computed;
        }

        public ComputedManifest Computed { get; private set; }
        public Dictionary<string, NodeKnowledge> KnowledgeByNodeId { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void Initialize(string storePath)
        {
        }

        public void Reload()
        {
        }

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
            var match = _modules.FirstOrDefault(module =>
                string.Equals(module.Id, input, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(module.Name, input, StringComparison.OrdinalIgnoreCase));
            if (match != null)
                return [match.Id, match.Name];

            if (strict)
                throw new InvalidOperationException($"missing node: {input}");

            return [input];
        }

        public void UpdateComputedDependencies(string moduleName, List<string> computedDependencies)
        {
            Computed.ModuleDependencies[moduleName] = computedDependencies;
        }
    }

    private sealed class ThrowingTopologyApplicationService : ITopoGraphApplicationService
    {
        private readonly List<TopologyModuleDefinition> _modules;

        public ThrowingTopologyApplicationService(List<TopologyModuleDefinition> modules)
        {
            _modules = modules;
        }

        public bool TopologyRequested { get; private set; }
        public bool ValidationRequested { get; private set; }

        public TopologySnapshot BuildTopology()
        {
            TopologyRequested = true;
            return new TopologySnapshot();
        }

        public TopologySnapshot GetTopology() => new();
        public TopologyWorkbenchSnapshot GetWorkbenchSnapshot() => new();

        public TopologyManagementSnapshot GetManagementSnapshot()
            => new()
            {
                Modules = _modules
            };

        public TopologyModuleKnowledgeView? GetModuleKnowledge(string nodeIdOrName) => throw new NotSupportedException();
        public IReadOnlyList<TopologyModuleKnowledgeView> ListModuleKnowledge() => throw new NotSupportedException();
        public TopologyModuleKnowledgeView SaveModuleKnowledge(TopologyModuleKnowledgeUpsertCommand command) => throw new NotSupportedException();
        public TopologyModuleRelationsView? GetModuleRelations(string nodeIdOrName) => throw new NotSupportedException();
        public ExecutionPlan GetExecutionPlan(List<string> moduleNames) => throw new NotSupportedException();
        public KnowledgeNode? FindModule(string nameOrPath) => throw new NotSupportedException();
        public List<KnowledgeNode> GetAllModules() => throw new NotSupportedException();
        public List<KnowledgeNode> GetModulesByDiscipline(string disciplineId) => throw new NotSupportedException();
        public ModuleContext GetModuleContext(string targetModule, string? currentModule, List<string>? activeModules = null) => throw new NotSupportedException();

        public GovernanceReport ValidateArchitecture()
        {
            ValidationRequested = true;
            return new GovernanceReport();
        }

        public List<CrossWork> GetCrossWorks() => throw new NotSupportedException();
        public List<CrossWork> GetCrossWorksForModule(string moduleName) => throw new NotSupportedException();
        public void RegisterModule(string discipline, TopologyModuleDefinition module) => throw new NotSupportedException();
        public bool UnregisterModule(string name) => throw new NotSupportedException();
        public void SaveCrossWork(TopologyCrossWorkDefinition crossWork) => throw new NotSupportedException();
        public bool RemoveCrossWork(string crossWorkId) => throw new NotSupportedException();
        public void UpsertDiscipline(string disciplineId, string? displayName, string roleId, List<LayerDefinition> layers) => throw new NotSupportedException();
        public bool RemoveDiscipline(string disciplineId) => throw new NotSupportedException();
        public string? GetDisciplineRoleId(string moduleName) => throw new NotSupportedException();
        public WorkspaceTopologyContext GetWorkspaceContext() => throw new NotSupportedException();
        public void ReloadManifests() => throw new NotSupportedException();
        public void Initialize(string storePath) => throw new NotSupportedException();
    }
}
