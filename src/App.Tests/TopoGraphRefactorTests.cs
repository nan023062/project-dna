using Dna.Knowledge;
using Dna.Knowledge.Workspace.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace App.Tests;

public sealed class TopoGraphRefactorTests
{
    [Fact]
    public void BuildTopology_ShouldProduceTypedRelations_AndKeepLegacyDependencyEdges()
    {
        var store = new FakeTopoGraphStore(
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
                            Summary = "Core module",
                            Boundary = "semi-open",
                            PublicApi = ["ICoreApi"],
                            Constraints = ["No UI"],
                            ManagedPaths = ["src/shared"],
                            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                            {
                                ["budget"] = "strict"
                            }
                        },
                        new ModuleRegistration
                        {
                            Id = "team-combat",
                            Name = "Combat",
                            Path = "src/combat",
                            Layer = 2,
                            ParentModuleId = "tech-core",
                            Dependencies = ["Core"],
                            Summary = "Combat feature"
                        },
                        new ModuleRegistration
                        {
                            Id = "team-delivery",
                            Name = "Delivery",
                            Path = "src/delivery",
                            Layer = 2,
                            IsCrossWorkModule = true,
                            Participants =
                            [
                                new CrossWorkParticipantRegistration
                                {
                                    ModuleName = "Combat",
                                    Role = "deliver"
                                },
                                new CrossWorkParticipantRegistration
                                {
                                    ModuleName = "Core",
                                    Role = "support"
                                }
                            ]
                        }
                    ]
                },
                CrossWorks =
                [
                    new CrossWorkRegistration
                    {
                        Id = "cw-liveops",
                        Name = "LiveOps",
                        Participants =
                        [
                            new CrossWorkParticipantRegistration
                            {
                                ModuleName = "Combat",
                                Role = "owner"
                            },
                            new CrossWorkParticipantRegistration
                            {
                                ModuleName = "Core",
                                Role = "support"
                            }
                        ]
                    }
                ]
            },
            new ComputedManifest
            {
                ModuleDependencies = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Combat"] = ["Core"]
                }
            });

        var graph = new GraphEngine(store, contextProvider: null, NullLogger<GraphEngine>.Instance);

        var topology = graph.BuildTopology();

        Assert.Contains(topology.Relations, relation =>
            relation.Type == TopologyRelationType.Containment &&
            relation.FromId == "tech-core" &&
            relation.ToId == "team-combat");

        Assert.Contains(topology.Relations, relation =>
            relation.Type == TopologyRelationType.Dependency &&
            relation.FromId == "team-combat" &&
            relation.ToId == "tech-core" &&
            relation.IsComputed == false);

        Assert.Contains(topology.Relations, relation =>
            relation.Type == TopologyRelationType.Collaboration &&
            relation.IsComputed);

        Assert.Contains(topology.Edges, edge =>
            edge.From == "Combat" &&
            edge.To == "Core" &&
            edge.IsComputed == false);

        Assert.Contains(topology.DepMap, pair =>
            pair.Key == "Combat" &&
            pair.Value.Contains("Core", StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildTopology_ShouldPopulateModuleContract_AndPathBinding()
    {
        var store = new FakeTopoGraphStore(
            new ModulesManifest
            {
                Disciplines = new Dictionary<string, List<ModuleRegistration>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["engineering"] =
                    [
                        new ModuleRegistration
                        {
                            Id = "tech-app",
                            Name = "AppFramework",
                            Path = "src/app/framework",
                            Layer = 1,
                            Boundary = "open",
                            PublicApi = ["IWindowHost", "IWorkspaceShell"],
                            Constraints = ["No direct business logic"],
                            ManagedPaths = ["src/app/shared", "src/app/contracts"],
                            Summary = "Framework runtime"
                        }
                    ]
                }
            },
            new ComputedManifest());

        var graph = new GraphEngine(store, contextProvider: null, NullLogger<GraphEngine>.Instance);
        var topology = graph.BuildTopology();
        var node = Assert.Single(topology.Nodes);

        Assert.Equal("src/app/framework", node.PathBinding.MainPath);
        Assert.Contains(node.PathBinding.ManagedPaths, value =>
            string.Equals(value, "src/app/shared", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("open", node.ContractInfo.Boundary);
        Assert.Contains(node.ContractInfo.PublicApi, value =>
            string.Equals(value, "IWindowHost", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(node.ContractInfo.Constraints, value =>
            string.Equals(value, "No direct business logic", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(node.PathBinding.MainPath, node.RelativePath);
        Assert.Equal(node.ContractInfo.Boundary, node.Boundary);
        Assert.Contains(node.PublicApi ?? [], value =>
            string.Equals(value, "IWorkspaceShell", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class FakeTopoGraphStore : ITopoGraphStore
    {
        private readonly ArchitectureManifest _architecture = new();
        private readonly Dictionary<string, NodeKnowledge> _knowledge = new(StringComparer.OrdinalIgnoreCase);

        public FakeTopoGraphStore(ModulesManifest manifest, ComputedManifest computed)
        {
            Manifest = manifest;
            Computed = computed;
        }

        public ModulesManifest Manifest { get; private set; }
        public ComputedManifest Computed { get; private set; }

        public void Initialize(string storePath)
        {
        }

        public void Reload()
        {
        }

        public ArchitectureManifest GetArchitecture() => _architecture;

        public ModulesManifest GetModulesManifest() => Manifest;

        public ComputedManifest GetComputedManifest() => Computed;

        public Dictionary<string, NodeKnowledge> LoadNodeKnowledgeMap() => _knowledge;

        public void UpsertNodeKnowledge(string nodeId, NodeKnowledge knowledge)
        {
            _knowledge[nodeId] = knowledge;
        }

        public List<string> ResolveNodeIdCandidates(string? nodeId, bool strict = false)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
                return [];

            return [nodeId.Trim()];
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
}
