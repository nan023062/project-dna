using Dna.Knowledge.TopoGraph.Models.Nodes;
using Dna.Knowledge.TopoGraph.Models.Snapshots;
using TopologyKnowledgeSummaryModel = Dna.Knowledge.TopoGraph.Models.ValueObjects.TopologyKnowledgeSummary;
using TopologyModuleContractModel = Dna.Knowledge.TopoGraph.Models.ValueObjects.ModuleContract;

namespace Dna.Knowledge;

public sealed partial class TopoGraphApplicationService
{
    private TopologySnapshot BuildCompatibilityTopology()
    {
        var snapshot = _facade.GetSnapshot();
        var computed = _store.GetComputedManifest();
        var knowledgeMap = _store.LoadNodeKnowledgeMap();
        var management = BuildManagementSnapshot(snapshot);

        var nodes = snapshot.Modules
            .Select(module => ToKnowledgeNode(module, snapshot, computed, knowledgeMap))
            .ToList();

        PopulateChildIds(nodes);

        var crossWorks = management.CrossWorks
            .Select(item => new CrossWork
            {
                Id = item.Id,
                Name = item.Name,
                Description = item.Description,
                Feature = item.Feature,
                Participants = item.Participants.Select(ToParticipant).ToList()
            })
            .ToList();
        var relations = BuildRelations(snapshot, nodes, crossWorks);
        var (depMap, rdepMap) = BuildDependencyMaps(relations, nodes);

        return new TopologySnapshot
        {
            Nodes = nodes,
            CrossWorks = crossWorks,
            Relations = relations,
            Edges = BuildLegacyDependencyEdges(relations, nodes),
            DepMap = depMap,
            RdepMap = rdepMap,
            BuiltAt = DateTime.UtcNow
        };
    }

    private TopologyManagementSnapshot BuildManagementSnapshot(TopologyModelSnapshot snapshot)
    {
        var disciplines = snapshot.Departments
            .Select(department => new TopologyDisciplineDefinition
            {
                Id = string.IsNullOrWhiteSpace(department.DisciplineCode) ? department.Name : department.DisciplineCode,
                DisplayName = department.Name,
                RoleId = string.IsNullOrWhiteSpace(department.RoleId) ? "coder" : department.RoleId,
                Layers = department.Layers.ToList()
            })
            .OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var modules = snapshot.Modules
            .Select(module => ToManagementModule(module, snapshot.NodeMap))
            .OrderBy(item => item.Discipline, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var crossWorks = BuildCrossWorks(modules, []);

        return new TopologyManagementSnapshot
        {
            ExcludeDirs = snapshot.Project?.ExcludeDirs.Where(item => !string.IsNullOrWhiteSpace(item)).ToList() ?? [],
            Disciplines = disciplines,
            Modules = modules,
            CrossWorks = crossWorks
                .Select(item => new TopologyCrossWorkDefinition
                {
                    Id = item.Id,
                    Name = item.Name,
                    Description = item.Description,
                    Feature = item.Feature,
                    Participants = item.Participants
                        .Select(participant => new TopologyCrossWorkParticipantDefinition
                        {
                            ModuleName = participant.ModuleName,
                            Role = participant.Role,
                            ContractType = participant.ContractType,
                            Contract = participant.Contract,
                            Deliverable = participant.Deliverable
                        })
                        .ToList()
                })
                .ToList()
        };
    }

    private KnowledgeNode ToKnowledgeNode(
        ModuleNode module,
        TopologyModelSnapshot snapshot,
        ComputedManifest computed,
        Dictionary<string, NodeKnowledge> knowledgeMap)
    {
        var discipline = ResolveDiscipline(module, snapshot.NodeMap);
        var memoryIdentity = _contextProvider?.GetIdentitySnapshot(module.Id);
        var mergedKnowledge = MergeKnowledge(module, knowledgeMap.GetValueOrDefault(module.Id));
        var declaredDependencies = GetDeclaredDependencies(module);
        var computedDependencies = GetComputedDependencies(module, computed);

        var node = new KnowledgeNode
        {
            Id = module.Id,
            Name = module.Name,
            Type = module.Kind == TopologyNodeKind.Team ? NodeType.Team : NodeType.Technical,
            ParentId = snapshot.NodeMap.TryGetValue(module.ParentId ?? string.Empty, out var parent) && parent is ModuleNode
                ? module.ParentId
                : null,
            Discipline = discipline,
            Maintainer = module.Maintainer,
            Summary = FirstNonEmpty(module.Summary, memoryIdentity?.Summary, mergedKnowledge.Identity),
            Metadata = module.Metadata.Count == 0 ? null : new Dictionary<string, string>(module.Metadata, StringComparer.OrdinalIgnoreCase),
            IsCrossWorkModule = module.IsCrossWorkModule,
            Layer = module.Layer,
            Dependencies = ResolveDependencyNames(declaredDependencies, snapshot.NodeMap),
            ComputedDependencies = ResolveDependencyNames(computedDependencies, snapshot.NodeMap),
            PathBinding = new ModulePathBinding
            {
                MainPath = NormalizePath(module.PathBinding.MainPath),
                ManagedPaths = module.PathBinding.GetAllPaths().ToList()
            },
            Knowledge = mergedKnowledge,
            Keywords = BuildKeywords(module, memoryIdentity),
            Contract = module is TechnicalNode technical ? BuildContractText(technical.Contract) : null
        };

        if (module is TechnicalNode technicalNode)
        {
            node.ContractInfo = new ModuleContract
            {
                Boundary = technicalNode.Contract.Boundary,
                PublicApi = [.. technicalNode.Contract.PublicApi],
                Constraints = [.. technicalNode.Contract.Constraints]
            };
        }

        return node;
    }

    private static TopologyModuleDefinition ToManagementModule(ModuleNode module, Dictionary<string, TopologyNode> nodeMap)
    {
        var discipline = ResolveDiscipline(module, nodeMap) ?? "root";
        return new TopologyModuleDefinition
        {
            Discipline = discipline,
            Id = module.Id,
            Name = module.Name,
            Path = NormalizePath(module.PathBinding.MainPath) ?? string.Empty,
            Layer = module.Layer,
            ParentModuleId = ResolveModuleParentId(module.ParentId, nodeMap),
            ManagedPaths = module.PathBinding.GetAllPaths().ToList(),
            IsCrossWorkModule = module.IsCrossWorkModule,
            Participants = module.Participants
                .Select(item => new TopologyCrossWorkParticipantDefinition
                {
                    ModuleName = item.ModuleName,
                    Role = item.Role,
                    ContractType = item.ContractType,
                    Contract = item.Contract,
                    Deliverable = item.Deliverable
                })
                .ToList(),
            Dependencies = GetDeclaredDependencies(module),
            Maintainer = module.Maintainer,
            Summary = module.Summary,
            Boundary = module is TechnicalNode technical ? technical.Contract.Boundary : null,
            PublicApi = module is TechnicalNode apiNode ? apiNode.Contract.PublicApi.ToList() : null,
            Constraints = module is TechnicalNode constraintNode ? constraintNode.Contract.Constraints.ToList() : null,
            Metadata = module.Metadata.Count == 0 ? null : new Dictionary<string, string>(module.Metadata, StringComparer.OrdinalIgnoreCase)
        };
    }

    private static string? ResolveModuleParentId(string? parentId, Dictionary<string, TopologyNode> nodeMap)
    {
        if (string.IsNullOrWhiteSpace(parentId))
            return null;

        return nodeMap.TryGetValue(parentId, out var parent) && parent is ModuleNode
            ? parent.Id
            : null;
    }

    private static NodeKnowledge MergeKnowledge(ModuleNode module, NodeKnowledge? persisted)
    {
        return new NodeKnowledge
        {
            Identity = persisted?.Identity ?? module.Knowledge.Identity,
            Lessons = persisted?.Lessons ?? module.Knowledge.Lessons.Select(item => new LessonSummary
            {
                Title = item.Title,
                Severity = item.Severity,
                Resolution = item.Resolution
            }).ToList(),
            ActiveTasks = persisted?.ActiveTasks ?? [],
            Facts = persisted?.Facts ?? [.. module.Knowledge.Facts],
            TotalMemoryCount = persisted?.TotalMemoryCount ?? 0,
            IdentityMemoryId = persisted?.IdentityMemoryId,
            UpgradeTrailMemoryId = persisted?.UpgradeTrailMemoryId,
            MemoryIds = persisted?.MemoryIds ?? [.. module.Knowledge.MemoryIds]
        };
    }

    private static List<string> GetDeclaredDependencies(ModuleNode module)
    {
        return module switch
        {
            TechnicalNode technical => [.. technical.DeclaredDependencies],
            TeamNode team => [.. team.TechnicalDependencies],
            _ => []
        };
    }

    private static List<string> GetComputedDependencies(ModuleNode module, ComputedManifest computed)
    {
        return module switch
        {
            TechnicalNode technical when technical.ComputedDependencies.Count > 0 => [.. technical.ComputedDependencies],
            TechnicalNode => computed.ModuleDependencies.GetValueOrDefault(module.Name, []),
            _ => []
        };
    }

    private static List<string> ResolveDependencyNames(IEnumerable<string> dependencyIds, Dictionary<string, TopologyNode> nodeMap)
    {
        return dependencyIds
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => nodeMap.TryGetValue(item, out var node) ? node.Name : item)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> BuildKeywords(ModuleNode module, TopoGraphIdentitySnapshot? memoryIdentity)
    {
        IEnumerable<string> typedKeywords = module switch
        {
            TechnicalNode technical => technical.CapabilityTags,
            TeamNode team => team.Deliverables,
            _ => []
        };

        return typedKeywords
            .Concat(memoryIdentity?.Keywords ?? [])
            .Where(keyword => !string.IsNullOrWhiteSpace(keyword))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? ResolveDiscipline(ModuleNode module, Dictionary<string, TopologyNode> nodeMap)
    {
        var currentParentId = module.ParentId;
        while (!string.IsNullOrWhiteSpace(currentParentId))
        {
            if (!nodeMap.TryGetValue(currentParentId, out var parent))
                break;

            if (parent is DepartmentNode department)
                return string.IsNullOrWhiteSpace(department.DisciplineCode) ? department.Name : department.DisciplineCode;

            currentParentId = parent.ParentId;
        }

        return "root";
    }

    private static void PopulateChildIds(List<KnowledgeNode> nodes)
    {
        var nodeIds = nodes
            .Where(node => !string.IsNullOrWhiteSpace(node.Id))
            .ToDictionary(node => node.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var node in nodes)
            node.ChildIds.Clear();

        foreach (var node in nodes)
        {
            if (string.IsNullOrWhiteSpace(node.ParentId))
                continue;

            if (nodeIds.TryGetValue(node.ParentId, out var parent))
                parent.ChildIds.Add(node.Id);
            else
                node.ParentId = null;
        }
    }

    private static List<CrossWork> BuildCrossWorks(
        IEnumerable<TopologyModuleDefinition> modules,
        IEnumerable<TopologyCrossWorkDefinition> explicitCrossWorks)
    {
        var result = explicitCrossWorks
            .Where(item => !string.IsNullOrWhiteSpace(item.Name) && item.Participants.Count > 0)
            .Select(item => new CrossWork
            {
                Id = item.Id,
                Name = item.Name,
                Description = item.Description,
                Feature = item.Feature,
                Participants = item.Participants
                    .Select(ToParticipant)
                    .ToList()
            })
            .ToList();

        foreach (var module in modules.Where(item => item.IsCrossWorkModule && item.Participants.Count > 0))
        {
            result.Add(new CrossWork
            {
                Id = module.Id,
                Name = module.Name,
                Description = module.Summary,
                Participants = module.Participants
                    .Select(ToParticipant)
                    .ToList()
            });
        }

        return result;
    }

    private static CrossWorkParticipant ToParticipant(TopologyCrossWorkParticipantDefinition participant)
    {
        return new CrossWorkParticipant
        {
            ModuleName = participant.ModuleName,
            Role = participant.Role,
            Contract = participant.Contract,
            ContractType = participant.ContractType,
            Deliverable = participant.Deliverable
        };
    }

    private static List<TopologyRelation> BuildRelations(
        TopologyModelSnapshot snapshot,
        List<KnowledgeNode> nodes,
        List<CrossWork> crossWorks)
    {
        var nodeIds = nodes
            .Where(node => !string.IsNullOrWhiteSpace(node.Id))
            .ToDictionary(node => node.Id, StringComparer.OrdinalIgnoreCase);
        var relations = new List<TopologyRelation>();

        foreach (var node in nodes.Where(node => !string.IsNullOrWhiteSpace(node.ParentId) && nodeIds.ContainsKey(node.ParentId)))
        {
            relations.Add(new TopologyRelation
            {
                FromId = node.ParentId!,
                ToId = node.Id,
                Type = TopologyRelationType.Containment,
                Label = TopoGraphConstants.Relations.ContainmentLabel
            });
        }

        foreach (var relation in snapshot.Dependencies
                     .Where(relation => nodeIds.ContainsKey(relation.FromId) && nodeIds.ContainsKey(relation.ToId)))
        {
            relations.Add(new TopologyRelation
            {
                FromId = relation.FromId,
                ToId = relation.ToId,
                Type = TopologyRelationType.Dependency,
                IsComputed = relation.IsComputed,
                Label = relation.Label
            });
        }

        var seenCollaborationKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var relation in snapshot.Collaborations
                     .Where(relation => nodeIds.ContainsKey(relation.FromId) && nodeIds.ContainsKey(relation.ToId)))
        {
            AddCollaborationRelation(relations, seenCollaborationKeys, relation.FromId, relation.ToId, relation.Label);
        }

        var nodesByName = nodes.ToDictionary(node => node.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var crossWork in crossWorks)
        {
            var participants = crossWork.Participants
                .Select(participant => nodesByName.GetValueOrDefault(participant.ModuleName))
                .Where(node => node != null)
                .Cast<KnowledgeNode>()
                .DistinctBy(node => node.Id)
                .ToList();

            for (var index = 0; index < participants.Count; index++)
            {
                for (var peerIndex = index + 1; peerIndex < participants.Count; peerIndex++)
                {
                    AddCollaborationRelation(
                        relations,
                        seenCollaborationKeys,
                        participants[index].Id,
                        participants[peerIndex].Id,
                        crossWork.Name);
                }
            }
        }

        return relations;
    }

    private static void AddCollaborationRelation(
        List<TopologyRelation> relations,
        HashSet<string> seen,
        string fromId,
        string toId,
        string? label)
    {
        if (string.IsNullOrWhiteSpace(fromId) || string.IsNullOrWhiteSpace(toId))
            return;

        var (left, right) = string.Compare(fromId, toId, StringComparison.OrdinalIgnoreCase) <= 0
            ? (fromId, toId)
            : (toId, fromId);

        var key = $"{left}|{right}";
        if (!seen.Add(key))
            return;

        relations.Add(new TopologyRelation
        {
            FromId = left,
            ToId = right,
            Type = TopologyRelationType.Collaboration,
            IsComputed = true,
            Label = label
        });
    }

    private static (Dictionary<string, List<string>> DepMap, Dictionary<string, List<string>> RdepMap) BuildDependencyMaps(
        List<TopologyRelation> relations,
        List<KnowledgeNode> nodes)
    {
        var nodeNames = nodes.ToDictionary(node => node.Id, node => node.Name, StringComparer.OrdinalIgnoreCase);
        var depMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var rdepMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var relation in relations.Where(relation => relation.Type == TopologyRelationType.Dependency))
        {
            if (!nodeNames.TryGetValue(relation.FromId, out var fromName) ||
                !nodeNames.TryGetValue(relation.ToId, out var toName))
            {
                continue;
            }

            depMap.TryAdd(fromName, []);
            rdepMap.TryAdd(toName, []);

            if (!depMap[fromName].Contains(toName, StringComparer.OrdinalIgnoreCase))
                depMap[fromName].Add(toName);
            if (!rdepMap[toName].Contains(fromName, StringComparer.OrdinalIgnoreCase))
                rdepMap[toName].Add(fromName);
        }

        return (depMap, rdepMap);
    }

    private static List<KnowledgeEdge> BuildLegacyDependencyEdges(List<TopologyRelation> relations, List<KnowledgeNode> nodes)
    {
        var nodeNames = nodes.ToDictionary(node => node.Id, node => node.Name, StringComparer.OrdinalIgnoreCase);
        return relations
            .Where(relation => relation.Type == TopologyRelationType.Dependency)
            .Where(relation => nodeNames.ContainsKey(relation.FromId) && nodeNames.ContainsKey(relation.ToId))
            .Select(relation => new KnowledgeEdge
            {
                From = nodeNames[relation.FromId],
                To = nodeNames[relation.ToId],
                IsComputed = relation.IsComputed
            })
            .ToList();
    }

    private static string? BuildContractText(TopologyModuleContractModel contract)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(contract.Boundary))
            parts.Add($"Boundary: {contract.Boundary.Trim()}");
        parts.AddRange(contract.PublicApi.Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => $"- {item.Trim()}"));
        return parts.Count == 0 ? null : string.Join(Environment.NewLine, parts);
    }
}
