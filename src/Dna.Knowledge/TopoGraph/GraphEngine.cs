using System.Text.Json;
using Dna.Core.Framework;
using Dna.Knowledge.Workspace.Models;
using Microsoft.Extensions.Logging;

namespace Dna.Knowledge;

internal static class ContextFilter
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    internal static ModuleContext BuildContext(
        string targetModule,
        string? currentModule,
        TopologySnapshot topology,
        ITopoGraphContextProvider? contextProvider,
        IProjectAdapter? adapter,
        List<string>? activeModules)
    {
        var targetNode = topology.Nodes.FirstOrDefault(node =>
            string.Equals(node.Name, targetModule, StringComparison.OrdinalIgnoreCase));

        if (targetNode == null)
        {
            return new ModuleContext
            {
                ModuleName = targetModule,
                Level = ContextLevel.Unlinked,
                BlockMessage = string.Format(TopoGraphConstants.Context.MissingModuleTemplate, targetModule)
            };
        }

        var level = DetermineLevel(targetModule, currentModule, topology, activeModules);
        if (level == ContextLevel.Unlinked)
        {
            return new ModuleContext
            {
                ModuleName = targetNode.Name,
                Discipline = targetNode.Discipline,
                Level = level,
                BlockMessage = string.Format(TopoGraphConstants.Context.BlockedModuleTemplate, targetModule)
            };
        }

        var contextContent = contextProvider?.GetContextContent(targetNode.Id) ?? BuildFallbackContext(targetNode);
        var linksContent = BuildLinksContent(targetNode);

        if (level == ContextLevel.CrossWorkPeer)
        {
            var crossWorkContract = BuildCrossWorkContract(targetModule, currentModule, topology);
            return new ModuleContext
            {
                ModuleName = targetNode.Name,
                Discipline = targetNode.Discipline,
                Level = level,
                ContractContent = crossWorkContract ?? contextContent.ContractContent
            };
        }

        return new ModuleContext
        {
            ModuleName = targetNode.Name,
            Discipline = targetNode.Discipline,
            Level = level,
            IdentityContent = contextContent.IdentityContent,
            LessonsContent = contextContent.LessonsContent,
            LinksContent = linksContent,
            ActiveContent = contextContent.ActiveContent,
            ContractContent = contextContent.ContractContent,
            ContentFilePaths = adapter?.GetModuleFiles(targetNode.RelativePath ?? string.Empty) ?? [],
            Summary = targetNode.Summary,
            Boundary = targetNode.Boundary,
            PublicApi = targetNode.PublicApi,
            Constraints = targetNode.Constraints,
            Metadata = targetNode.Metadata
        };
    }

    private static TopoGraphContextContent BuildFallbackContext(KnowledgeNode node)
    {
        return new TopoGraphContextContent
        {
            IdentityContent = node.Knowledge.Identity,
            LessonsContent = node.Knowledge.Lessons.Count == 0
                ? null
                : string.Join("\n", node.Knowledge.Lessons.Select(item => $"- {item.Title}")),
            ActiveContent = node.Knowledge.ActiveTasks.Count == 0
                ? null
                : string.Join("\n", node.Knowledge.ActiveTasks.Select(item => $"- {item}")),
            ContractContent = node.Contract
        };
    }

    private static ContextLevel DetermineLevel(
        string target,
        string? current,
        TopologySnapshot topology,
        List<string>? activeModules)
    {
        if (string.IsNullOrEmpty(current))
            return ContextLevel.SharedOrSoft;

        if (string.Equals(target, current, StringComparison.OrdinalIgnoreCase))
            return ContextLevel.Current;

        if (activeModules?.Contains(target, StringComparer.OrdinalIgnoreCase) == true)
            return ContextLevel.Current;

        var currentNode = topology.Nodes.FirstOrDefault(node =>
            string.Equals(node.Name, current, StringComparison.OrdinalIgnoreCase));
        var targetNode = topology.Nodes.FirstOrDefault(node =>
            string.Equals(node.Name, target, StringComparison.OrdinalIgnoreCase));

        if (currentNode != null && currentNode.IsCrossWorkModule)
        {
            if (targetNode is { IsCrossWorkModule: true })
                return ContextLevel.Unlinked;

            return ContextLevel.Current;
        }

        if (targetNode is { IsCrossWorkModule: true })
            return ContextLevel.Unlinked;

        var hasDependency = topology.DepMap.TryGetValue(current, out var dependencies) &&
                            dependencies.Contains(target, StringComparer.OrdinalIgnoreCase);
        if (hasDependency)
            return ContextLevel.SharedOrSoft;

        var isCrossWorkPeer = topology.CrossWorks.Any(crossWork =>
            crossWork.Participants.Any(participant => string.Equals(participant.ModuleName, current, StringComparison.OrdinalIgnoreCase)) &&
            crossWork.Participants.Any(participant => string.Equals(participant.ModuleName, target, StringComparison.OrdinalIgnoreCase)));
        if (isCrossWorkPeer)
            return ContextLevel.CrossWorkPeer;

        return ContextLevel.Unlinked;
    }

    private static string? BuildCrossWorkContract(string target, string? current, TopologySnapshot topology)
    {
        if (string.IsNullOrEmpty(current))
            return null;

        var sharedCrossWorks = topology.CrossWorks
            .Where(crossWork =>
                crossWork.Participants.Any(participant => string.Equals(participant.ModuleName, current, StringComparison.OrdinalIgnoreCase)) &&
                crossWork.Participants.Any(participant => string.Equals(participant.ModuleName, target, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (sharedCrossWorks.Count == 0)
            return null;

        var parts = new List<string>();
        foreach (var crossWork in sharedCrossWorks)
        {
            var participant = crossWork.Participants.First(item =>
                string.Equals(item.ModuleName, target, StringComparison.OrdinalIgnoreCase));

            var section = $"{TopoGraphConstants.Context.CrossWorkSectionHeadingPrefix}{crossWork.Name}\n" +
                          $"{TopoGraphConstants.Context.ResponsibilityLinePrefix}{participant.Role}\n" +
                          (participant.Contract is not null
                              ? $"{TopoGraphConstants.Context.ContractLinePrefix}{participant.Contract}\n"
                              : string.Empty) +
                          (participant.Deliverable is not null
                              ? $"{TopoGraphConstants.Context.DeliverableLinePrefix}{participant.Deliverable}\n"
                              : string.Empty);
            parts.Add(section);
        }

        return string.Join("\n", parts);
    }

    private static string BuildLinksContent(KnowledgeNode node)
    {
        var allDependencies = node.Dependencies
            .Union(node.ComputedDependencies, StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item)
            .ToList();
        if (allDependencies.Count == 0)
            return TopoGraphConstants.Context.EmptyLinksJson;

        var declared = new HashSet<string>(node.Dependencies, StringComparer.OrdinalIgnoreCase);
        var computed = new HashSet<string>(node.ComputedDependencies, StringComparer.OrdinalIgnoreCase);
        var parts = allDependencies.Select(dependency => new
        {
            name = dependency,
            declared = declared.Contains(dependency),
            computed = computed.Contains(dependency)
        });

        return JsonSerializer.Serialize(parts, JsonOpts);
    }
}

internal static class GovernanceAnalyzer
{
    internal static GovernanceReport Analyze(TopologySnapshot topo, IProjectAdapter? adapter)
    {
        var cycles = DetectAllCycles(topo);
        var orphans = DetectOrphanNodes(topo);
        var crossWorkIssues = ValidateCrossWorks(topo, adapter);
        var depDrifts = DetectDependencyDrifts(topo);
        var keyNodeWarnings = DetectKeyNodes(topo);

        return new GovernanceReport
        {
            CycleSuggestions = cycles,
            OrphanNodes = orphans,
            CrossWorkIssues = crossWorkIssues,
            DependencyDrifts = depDrifts,
            KeyNodeWarnings = keyNodeWarnings
        };
    }

    private static Dictionary<string, KnowledgeNode> SafeNodeMap(IReadOnlyList<KnowledgeNode> nodes)
    {
        var map = new Dictionary<string, KnowledgeNode>(nodes.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var node in nodes)
            map.TryAdd(node.Name, node);

        return map;
    }

    private static List<CycleSuggestion> DetectAllCycles(TopologySnapshot topo)
    {
        var adj = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in topo.Nodes)
            adj[node.Name] = [];

        foreach (var relation in topo.DependencyRelations)
        {
            var fromName = ResolveRelationNodeName(relation.FromId, topo.Nodes);
            var toName = ResolveRelationNodeName(relation.ToId, topo.Nodes);
            if (string.IsNullOrWhiteSpace(fromName) || string.IsNullOrWhiteSpace(toName))
                continue;

            adj.GetValueOrDefault(fromName, []).Add(toName);
        }

        var index = 0;
        var stack = new Stack<string>();
        var onStack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var indices = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var lowLinks = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var sccs = new List<List<string>>();

        foreach (var node in topo.Nodes)
        {
            if (!indices.ContainsKey(node.Name))
                Tarjan(node.Name, adj, ref index, stack, onStack, indices, lowLinks, sccs);
        }

        return sccs
            .Where(scc => scc.Count > 1)
            .Select(scc => new CycleSuggestion
            {
                CycleMembers = scc,
                Message = string.Format(TopoGraphConstants.Governance.CycleMessageTemplate, string.Join(" -> ", scc), scc[0]),
                Suggestion = TopoGraphConstants.Governance.CycleSuggestion
            })
            .ToList();
    }

    private static void Tarjan(
        string current,
        Dictionary<string, List<string>> adj,
        ref int index,
        Stack<string> stack,
        HashSet<string> onStack,
        Dictionary<string, int> indices,
        Dictionary<string, int> lowLinks,
        List<List<string>> sccs)
    {
        indices[current] = index;
        lowLinks[current] = index;
        index++;
        stack.Push(current);
        onStack.Add(current);

        foreach (var next in adj.GetValueOrDefault(current, []))
        {
            if (!indices.ContainsKey(next))
            {
                Tarjan(next, adj, ref index, stack, onStack, indices, lowLinks, sccs);
                lowLinks[current] = Math.Min(lowLinks[current], lowLinks[next]);
            }
            else if (onStack.Contains(next))
            {
                lowLinks[current] = Math.Min(lowLinks[current], indices[next]);
            }
        }

        if (lowLinks[current] != indices[current])
            return;

        var scc = new List<string>();
        string nodeName;
        do
        {
            nodeName = stack.Pop();
            onStack.Remove(nodeName);
            scc.Add(nodeName);
        } while (!string.Equals(nodeName, current, StringComparison.OrdinalIgnoreCase));

        sccs.Add(scc);
    }

    private static List<KnowledgeNode> DetectOrphanNodes(TopologySnapshot topo)
    {
        var connected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var relation in topo.DependencyRelations)
        {
            var fromName = ResolveRelationNodeName(relation.FromId, topo.Nodes);
            var toName = ResolveRelationNodeName(relation.ToId, topo.Nodes);
            if (!string.IsNullOrWhiteSpace(fromName))
                connected.Add(fromName);
            if (!string.IsNullOrWhiteSpace(toName))
                connected.Add(toName);
        }

        foreach (var crossWork in topo.CrossWorks)
        {
            foreach (var participant in crossWork.Participants)
                connected.Add(participant.ModuleName);
        }

        return topo.Nodes
            .Where(node => !node.IsCrossWorkModule && !connected.Contains(node.Name))
            .ToList();
    }

    private static List<CrossWorkIssue> ValidateCrossWorks(TopologySnapshot topo, IProjectAdapter? adapter)
    {
        var issues = new List<CrossWorkIssue>();
        var nodeMap = SafeNodeMap(topo.Nodes);
        var participantNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var crossWork in topo.CrossWorks)
        {
            participantNames.Clear();
            foreach (var participant in crossWork.Participants)
            {
                participantNames.Add(participant.ModuleName);

                if (!nodeMap.TryGetValue(participant.ModuleName, out var node))
                {
                    issues.Add(new CrossWorkIssue
                    {
                        CrossWorkId = crossWork.Id,
                        CrossWorkName = crossWork.Name,
                        Message = string.Format(TopoGraphConstants.Governance.MissingParticipantTemplate, participant.ModuleName)
                    });
                    continue;
                }

                if (string.IsNullOrWhiteSpace(participant.Contract) &&
                    string.IsNullOrWhiteSpace(participant.Deliverable))
                {
                    issues.Add(new CrossWorkIssue
                    {
                        CrossWorkId = crossWork.Id,
                        CrossWorkName = crossWork.Name,
                        Message = string.Format(TopoGraphConstants.Governance.MissingContractOrDeliverableTemplate, participant.ModuleName)
                    });
                }

                if (adapter != null && !string.IsNullOrWhiteSpace(participant.ContractType))
                {
                    var result = adapter.ValidateContract(participant, node);
                    if (!result.IsValid)
                    {
                        issues.Add(new CrossWorkIssue
                        {
                            CrossWorkId = crossWork.Id,
                            CrossWorkName = crossWork.Name,
                            Message = string.Format(
                                TopoGraphConstants.Governance.ContractValidationFailedTemplate,
                                participant.ModuleName,
                                string.Join(", ", result.Errors))
                        });
                    }
                }
            }

            var hasDirectDependency = topo.DependencyRelations.Any(relation =>
            {
                var fromName = ResolveRelationNodeName(relation.FromId, topo.Nodes);
                var toName = ResolveRelationNodeName(relation.ToId, topo.Nodes);
                return !string.IsNullOrWhiteSpace(fromName) &&
                       !string.IsNullOrWhiteSpace(toName) &&
                       participantNames.Contains(fromName) &&
                       participantNames.Contains(toName);
            });

            if (hasDirectDependency)
            {
                issues.Add(new CrossWorkIssue
                {
                    CrossWorkId = crossWork.Id,
                    CrossWorkName = crossWork.Name,
                    Message = TopoGraphConstants.Governance.DirectDependencyMessage
                });
            }
        }

        return issues;
    }

    private static List<DependencyDriftIssue> DetectDependencyDrifts(TopologySnapshot topo)
    {
        var issues = new List<DependencyDriftIssue>();
        var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var computed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var declaredOnly = new List<string>();
        var computedOnly = new List<string>();

        foreach (var node in topo.Nodes)
        {
            declared.Clear();
            computed.Clear();
            declaredOnly.Clear();
            computedOnly.Clear();

            foreach (var dependency in node.Dependencies)
                declared.Add(dependency);
            foreach (var dependency in node.ComputedDependencies)
                computed.Add(dependency);

            if (declared.SetEquals(computed))
                continue;

            foreach (var dependency in declared)
            {
                if (!computed.Contains(dependency))
                    declaredOnly.Add(dependency);
            }

            foreach (var dependency in computed)
            {
                if (!declared.Contains(dependency))
                    computedOnly.Add(dependency);
            }

            declaredOnly.Sort(StringComparer.OrdinalIgnoreCase);
            computedOnly.Sort(StringComparer.OrdinalIgnoreCase);

            var parts = new List<string>();
            if (declaredOnly.Count > 0)
                parts.Add(string.Format(TopoGraphConstants.Governance.DeclaredOnlyTemplate, string.Join(", ", declaredOnly)));

            if (computedOnly.Count > 0)
                parts.Add(string.Format(TopoGraphConstants.Governance.ComputedOnlyTemplate, string.Join(", ", computedOnly)));

            issues.Add(new DependencyDriftIssue
            {
                ModuleName = node.Name,
                Message = string.Format(TopoGraphConstants.Governance.DependencyDriftMessageTemplate, string.Join("; ", parts)),
                DeclaredOnly = [.. declaredOnly],
                ComputedOnly = [.. computedOnly],
                Suggestion = declaredOnly.Count > 0 && computedOnly.Count > 0
                    ? TopoGraphConstants.Governance.SyncDependenciesSuggestion
                    : declaredOnly.Count > 0
                        ? TopoGraphConstants.Governance.RemoveUnusedDependenciesSuggestion
                        : TopoGraphConstants.Governance.AddMissingDependenciesSuggestion
            });
        }

        return issues;
    }

    private static List<KeyNodeWarning> DetectKeyNodes(TopologySnapshot topo)
    {
        var warnings = new List<KeyNodeWarning>();

        foreach (var (nodeName, dependents) in topo.RdepMap)
        {
            if (dependents.Count < TopoGraphConstants.Governance.KeyNodeThreshold)
                continue;

            warnings.Add(new KeyNodeWarning
            {
                NodeName = nodeName,
                DependentCount = dependents.Count,
                Message = string.Format(TopoGraphConstants.Governance.KeyNodeWarningTemplate, nodeName, dependents.Count)
            });
        }

        return warnings;
    }

    private static string? ResolveRelationNodeName(string relationNodeId, IReadOnlyList<KnowledgeNode> nodes)
    {
        if (string.IsNullOrWhiteSpace(relationNodeId))
            return null;

        var exactById = nodes.FirstOrDefault(node =>
            string.Equals(node.Id, relationNodeId, StringComparison.OrdinalIgnoreCase));
        return exactById?.Name ?? relationNodeId;
    }
}

internal static class TopologyBuilder
{
    internal static TopologySnapshot Build(ITopoGraphStore store, ITopoGraphContextProvider? contextProvider)
    {
        var manifest = store.GetModulesManifest();
        var computed = store.GetComputedManifest();
        var nodes = BuildNodes(manifest, computed, store, contextProvider);
        var crossWorks = ExtractCrossWorks(manifest);
        var relations = BuildRelations(nodes, crossWorks);
        var edges = BuildLegacyDependencyEdges(nodes, relations);
        var (depMap, rdepMap) = BuildDepMaps(relations, nodes);

        return new TopologySnapshot
        {
            Nodes = nodes,
            Relations = relations,
            Edges = edges,
            DepMap = depMap,
            RdepMap = rdepMap,
            CrossWorks = crossWorks,
            BuiltAt = DateTime.UtcNow
        };
    }

    internal static ExecutionPlan GetExecutionPlan(TopologySnapshot topology, List<string> moduleNames)
    {
        var subset = new HashSet<string>(moduleNames, StringComparer.OrdinalIgnoreCase);
        var inDegree = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var adj = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in subset)
        {
            inDegree[name] = 0;
            adj[name] = [];
        }

        foreach (var edge in topology.Edges)
        {
            if (!subset.Contains(edge.From) || !subset.Contains(edge.To))
                continue;

            adj[edge.To].Add(edge.From);
            inDegree[edge.From] = inDegree.GetValueOrDefault(edge.From) + 1;
        }

        var queue = new Queue<string>(inDegree.Where(pair => pair.Value == 0).Select(pair => pair.Key));
        var ordered = new List<string>();

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            ordered.Add(current);
            foreach (var neighbor in adj.GetValueOrDefault(current, []))
            {
                inDegree[neighbor]--;
                if (inDegree[neighbor] == 0)
                    queue.Enqueue(neighbor);
            }
        }

        if (ordered.Count >= subset.Count)
            return new ExecutionPlan { OrderedModules = ordered };

        var stuck = subset.Except(ordered).ToList();
        return new ExecutionPlan
        {
            OrderedModules = ordered,
            HasCycle = true,
            CycleDescription = string.Format(TopoGraphConstants.ExecutionPlan.CycleDescriptionTemplate, string.Join(", ", stuck))
        };
    }

    private static List<KnowledgeNode> BuildNodes(
        ModulesManifest manifest,
        ComputedManifest computed,
        ITopoGraphStore store,
        ITopoGraphContextProvider? contextProvider)
    {
        var nodes = new List<KnowledgeNode>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var nodeKnowledgeMap = store.LoadNodeKnowledgeMap();
        var moduleById = manifest.Disciplines
            .SelectMany(item => item.Value)
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        var moduleByName = manifest.Disciplines
            .SelectMany(item => item.Value)
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var (discipline, modules) in manifest.Disciplines)
        {
            foreach (var registration in modules)
            {
                if (!seenNames.Add(registration.Name))
                    continue;

                var node = new KnowledgeNode
                {
                    Id = registration.Id,
                    Name = registration.Name,
                    Type = registration.IsCrossWorkModule ? NodeType.Team : NodeType.Technical,
                    ParentId = ResolveParentModuleId(registration.ParentModuleId, moduleById, moduleByName),
                    Layer = registration.Layer,
                    Maintainer = registration.Maintainer,
                    Summary = registration.Summary,
                    Metadata = registration.Metadata,
                    Discipline = discipline,
                    IsCrossWorkModule = registration.IsCrossWorkModule,
                    Dependencies = registration.IsCrossWorkModule ? [] : registration.Dependencies.ToList(),
                    ComputedDependencies = registration.IsCrossWorkModule
                        ? []
                        : computed.ModuleDependencies.GetValueOrDefault(registration.Name, []),
                    ContractInfo = BuildModuleContract(registration),
                    PathBinding = BuildPathBinding(registration),
                    Knowledge = nodeKnowledgeMap.GetValueOrDefault(registration.Id, new NodeKnowledge())
                };

                if (string.IsNullOrWhiteSpace(node.Summary) && !string.IsNullOrWhiteSpace(node.Knowledge.Identity))
                    node.Summary = node.Knowledge.Identity;

                var identity = contextProvider?.GetIdentitySnapshot(registration.Id);
                if (identity != null)
                {
                    if (!string.IsNullOrWhiteSpace(identity.Summary))
                        node.Summary = identity.Summary;

                    node.Contract = identity.Contract;

                    if (!string.IsNullOrWhiteSpace(identity.Description))
                    {
                        node.Metadata ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        node.Metadata[TopoGraphConstants.Metadata.IdentityDescription] = identity.Description;
                    }

                    if (identity.Keywords.Count > 0)
                    {
                        node.Keywords = identity.Keywords
                            .Where(keyword => !string.IsNullOrWhiteSpace(keyword))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();
                    }

                    if (string.IsNullOrWhiteSpace(node.Knowledge.Identity))
                        node.Knowledge.Identity = identity.Summary;
                }

                nodes.Add(node);
            }
        }

        var nodeById = nodes
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var node in nodes)
            node.ChildIds.Clear();

        foreach (var node in nodes)
        {
            if (string.IsNullOrWhiteSpace(node.ParentId))
                continue;

            if (!nodeById.ContainsKey(node.ParentId))
            {
                node.ParentId = null;
                continue;
            }

            nodeById[node.ParentId].ChildIds.Add(ResolveRelationNodeId(node));
        }

        return nodes;
    }

    private static List<TopologyRelation> BuildRelations(List<KnowledgeNode> nodes, List<CrossWork> crossWorks)
    {
        var relations = new List<TopologyRelation>();
        relations.AddRange(BuildContainmentRelations(nodes));
        relations.AddRange(BuildDependencyRelations(nodes));
        relations.AddRange(BuildCollaborationRelations(nodes, crossWorks));
        return relations;
    }

    private static List<TopologyRelation> BuildContainmentRelations(List<KnowledgeNode> nodes)
    {
        var relations = new List<TopologyRelation>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var nodeById = nodes
            .Where(node => !string.IsNullOrWhiteSpace(node.Id))
            .ToDictionary(node => node.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var node in nodes)
        {
            if (string.IsNullOrWhiteSpace(node.ParentId) || !nodeById.ContainsKey(node.ParentId))
                continue;

            var fromId = node.ParentId;
            var toId = ResolveRelationNodeId(node);
            if (string.IsNullOrWhiteSpace(fromId) || string.IsNullOrWhiteSpace(toId))
                continue;

            var key = $"{fromId}|{toId}|{TopologyRelationType.Containment}";
            if (!seen.Add(key))
                continue;

            relations.Add(new TopologyRelation
            {
                FromId = fromId,
                ToId = toId,
                Type = TopologyRelationType.Containment,
                Label = TopoGraphConstants.Relations.ContainmentLabel
            });
        }

        return relations;
    }

    private static List<TopologyRelation> BuildDependencyRelations(List<KnowledgeNode> nodes)
    {
        var nodeMap = new HashSet<string>(nodes.Select(node => node.Name), StringComparer.OrdinalIgnoreCase);
        var nodeByName = nodes
            .Where(node => !string.IsNullOrWhiteSpace(node.Name))
            .ToDictionary(node => node.Name, StringComparer.OrdinalIgnoreCase);
        var merged = new Dictionary<string, TopologyRelation>(StringComparer.OrdinalIgnoreCase);

        foreach (var node in nodes)
        {
            foreach (var dependency in node.ComputedDependencies)
            {
                if (!nodeMap.Contains(dependency))
                    continue;

                var fromId = ResolveRelationNodeId(node);
                var toId = ResolveRelationNodeId(nodeByName[dependency]);
                var key = $"{fromId}|{toId}|{TopologyRelationType.Dependency}";
                merged[key] = new TopologyRelation
                {
                    FromId = fromId,
                    ToId = toId,
                    Type = TopologyRelationType.Dependency,
                    IsComputed = true
                };
            }

            foreach (var dependency in node.Dependencies)
            {
                if (!nodeMap.Contains(dependency))
                    continue;

                var fromId = ResolveRelationNodeId(node);
                var toId = ResolveRelationNodeId(nodeByName[dependency]);
                var key = $"{fromId}|{toId}|{TopologyRelationType.Dependency}";
                merged[key] = new TopologyRelation
                {
                    FromId = fromId,
                    ToId = toId,
                    Type = TopologyRelationType.Dependency
                };
            }
        }

        return merged.Values.ToList();
    }

    private static List<TopologyRelation> BuildCollaborationRelations(List<KnowledgeNode> nodes, List<CrossWork> crossWorks)
    {
        var relations = new List<TopologyRelation>();
        var nodeByName = nodes
            .Where(node => !string.IsNullOrWhiteSpace(node.Name))
            .ToDictionary(node => node.Name, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var crossWork in crossWorks)
        {
            var seenParticipantIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var participants = crossWork.Participants
                .Select(participant => nodeByName.GetValueOrDefault(participant.ModuleName))
                .Where(node => node != null)
                .Cast<KnowledgeNode>()
                .Where(node => seenParticipantIds.Add(ResolveRelationNodeId(node)))
                .ToList();

            if (participants.Count < 2)
                continue;

            var crossWorkNode = nodeByName.GetValueOrDefault(crossWork.Name);
            if (crossWorkNode != null)
            {
                foreach (var participant in participants)
                {
                    if (string.Equals(crossWorkNode.Id, participant.Id, StringComparison.OrdinalIgnoreCase))
                        continue;

                    AddCollaborationRelation(
                        relations,
                        seen,
                        ResolveRelationNodeId(crossWorkNode),
                        ResolveRelationNodeId(participant),
                        crossWork.Name);
                }

                continue;
            }

            for (var index = 0; index < participants.Count; index++)
            {
                for (var peerIndex = index + 1; peerIndex < participants.Count; peerIndex++)
                {
                    AddCollaborationRelation(
                        relations,
                        seen,
                        ResolveRelationNodeId(participants[index]),
                        ResolveRelationNodeId(participants[peerIndex]),
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

        var key = $"{left}|{right}|{TopologyRelationType.Collaboration}";
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

    private static List<CrossWork> ExtractCrossWorks(ModulesManifest manifest)
    {
        var result = new List<CrossWork>();

        foreach (var crossWork in manifest.CrossWorks)
        {
            if (string.IsNullOrWhiteSpace(crossWork.Name) || crossWork.Participants.Count < 2)
                continue;

            result.Add(new CrossWork
            {
                Id = crossWork.Id,
                Name = crossWork.Name,
                Description = crossWork.Description,
                Feature = crossWork.Feature,
                Participants = crossWork.Participants
                    .Select(participant => new CrossWorkParticipant
                    {
                        ModuleName = participant.ModuleName,
                        Role = participant.Role,
                        Contract = participant.Contract,
                        ContractType = participant.ContractType,
                        Deliverable = participant.Deliverable
                    })
                    .ToList()
            });
        }

        foreach (var (_, modules) in manifest.Disciplines)
        {
            foreach (var registration in modules.Where(module => module.IsCrossWorkModule && module.Participants.Count > 0))
            {
                result.Add(new CrossWork
                {
                    Id = registration.Id,
                    Name = registration.Name,
                    Participants = registration.Participants
                        .Select(participant => new CrossWorkParticipant
                        {
                            ModuleName = participant.ModuleName,
                            Role = participant.Role,
                            Contract = participant.Contract,
                            ContractType = participant.ContractType,
                            Deliverable = participant.Deliverable
                        })
                        .ToList()
                });
            }
        }

        return result;
    }

    private static (Dictionary<string, List<string>> DepMap, Dictionary<string, List<string>> RdepMap) BuildDepMaps(
        List<TopologyRelation> relations,
        List<KnowledgeNode> nodes)
    {
        var dep = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var rdep = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var nodeNamesById = nodes
            .Where(node => !string.IsNullOrWhiteSpace(node.Id))
            .ToDictionary(node => node.Id, node => node.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var relation in relations.Where(item => item.Type == TopologyRelationType.Dependency))
        {
            var fromName = ResolveRelationNodeName(relation.FromId, nodeNamesById);
            var toName = ResolveRelationNodeName(relation.ToId, nodeNamesById);
            if (string.IsNullOrWhiteSpace(fromName) || string.IsNullOrWhiteSpace(toName))
                continue;

            if (!dep.TryGetValue(fromName, out var downstream))
            {
                downstream = [];
                dep[fromName] = downstream;
            }

            downstream.Add(toName);

            if (!rdep.TryGetValue(toName, out var upstream))
            {
                upstream = [];
                rdep[toName] = upstream;
            }

            upstream.Add(fromName);
        }

        return (dep, rdep);
    }

    private static string? ResolveParentModuleId(
        string? rawParentModuleId,
        Dictionary<string, ModuleRegistration> moduleById,
        Dictionary<string, ModuleRegistration> moduleByName)
    {
        if (string.IsNullOrWhiteSpace(rawParentModuleId))
            return null;

        var normalized = rawParentModuleId.Trim();
        if (moduleById.TryGetValue(normalized, out var byId))
            return byId.Id;
        if (moduleByName.TryGetValue(normalized, out var byName))
            return byName.Id;

        return normalized;
    }

    private static List<string> NormalizeManagedPaths(ModuleRegistration registration)
    {
        var values = new List<string>();

        void AddPath(string? raw)
        {
            var normalized = NormalizePath(raw);
            if (string.IsNullOrWhiteSpace(normalized))
                return;

            if (!values.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                values.Add(normalized);
        }

        AddPath(registration.Path);
        if (registration.ManagedPaths is { Count: > 0 })
        {
            foreach (var path in registration.ManagedPaths)
                AddPath(path);
        }

        return values;
    }

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var normalized = path.Replace('\\', '/').Trim().Trim('/');
        return normalized.Length == 0 ? null : normalized;
    }

    private static ModuleContract BuildModuleContract(ModuleRegistration registration)
    {
        return new ModuleContract
        {
            Boundary = registration.Boundary,
            PublicApi = registration.PublicApi?.ToList() ?? [],
            Constraints = registration.Constraints?.ToList() ?? []
        };
    }

    private static ModulePathBinding BuildPathBinding(ModuleRegistration registration)
    {
        return new ModulePathBinding
        {
            MainPath = NormalizePath(registration.Path),
            ManagedPaths = NormalizeManagedPaths(registration)
        };
    }

    private static string ResolveRelationNodeId(KnowledgeNode node)
        => !string.IsNullOrWhiteSpace(node.Id) ? node.Id : node.Name;

    private static string ResolveRelationNodeName(string relationNodeId, Dictionary<string, string> nodeNamesById)
        => nodeNamesById.TryGetValue(relationNodeId, out var name) ? name : relationNodeId;

    private static List<KnowledgeEdge> BuildLegacyDependencyEdges(List<KnowledgeNode> nodes, List<TopologyRelation> relations)
    {
        var nodeNamesById = nodes
            .Where(node => !string.IsNullOrWhiteSpace(node.Id))
            .ToDictionary(node => node.Id, node => node.Name, StringComparer.OrdinalIgnoreCase);
        var edges = new List<KnowledgeEdge>();

        foreach (var relation in relations.Where(item => item.Type == TopologyRelationType.Dependency))
        {
            var fromName = ResolveRelationNodeName(relation.FromId, nodeNamesById);
            var toName = ResolveRelationNodeName(relation.ToId, nodeNamesById);
            if (string.IsNullOrWhiteSpace(fromName) || string.IsNullOrWhiteSpace(toName))
                continue;

            edges.Add(new KnowledgeEdge
            {
                From = fromName,
                To = toName,
                IsComputed = relation.IsComputed
            });
        }

        return edges;
    }
}

public sealed class GraphEngine : IGraphEngine, IDnaService, IDisposable
{
    private readonly ITopoGraphStore _store;
    private readonly ITopoGraphContextProvider? _contextProvider;
    private readonly ILogger<GraphEngine> _logger;
    private IProjectAdapter? _adapter;
    private TopologySnapshot _topology = new();
    private readonly object _lock = new();

    public string ServiceName => TopoGraphConstants.Services.GraphEngine;

    public GraphEngine(ITopoGraphStore store, ITopoGraphContextProvider? contextProvider, ILogger<GraphEngine> logger)
    {
        _store = store;
        _contextProvider = contextProvider;
        _logger = logger;
    }

    public void SetAdapter(IProjectAdapter adapter) => _adapter = adapter;

    public TopologySnapshot BuildTopology()
    {
        lock (_lock)
        {
            _topology = TopologyBuilder.Build(_store, _contextProvider);

            if (_adapter != null)
            {
                var hasUpdates = false;
                foreach (var node in _topology.Nodes)
                {
                    var computed = _adapter.ComputeDependencies(node, _topology.Nodes);
                    if (computed.Count == 0)
                        continue;

                    _store.UpdateComputedDependencies(node.Name, computed);
                    hasUpdates = true;
                }

                if (hasUpdates)
                    _topology = TopologyBuilder.Build(_store, _contextProvider);
            }

            _logger.LogInformation(
                TopoGraphConstants.Logging.TopologySummary,
                _topology.Nodes.Count,
                _topology.Relations.Count,
                _topology.Edges.Count,
                _topology.CrossWorks.Count);

            return _topology;
        }
    }

    public TopologySnapshot GetTopology()
    {
        lock (_lock)
            return _topology;
    }

    public ExecutionPlan GetExecutionPlan(List<string> moduleNames)
    {
        lock (_lock)
            return TopologyBuilder.GetExecutionPlan(_topology, moduleNames);
    }

    public KnowledgeNode? FindModule(string nameOrPath)
    {
        lock (_lock)
        {
            return _topology.Nodes.FirstOrDefault(node =>
                string.Equals(node.Name, nameOrPath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(node.RelativePath, nameOrPath.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase));
        }
    }

    public List<KnowledgeNode> GetAllModules()
    {
        lock (_lock)
            return _topology.Nodes;
    }

    public List<KnowledgeNode> GetModulesByDiscipline(string disciplineId)
    {
        lock (_lock)
        {
            return _topology.Nodes
                .Where(node => string.Equals(node.Discipline, disciplineId, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }

    public ModuleContext GetModuleContext(string targetModule, string? currentModule, List<string>? activeModules = null)
    {
        lock (_lock)
        {
            return ContextFilter.BuildContext(targetModule, currentModule, _topology, _contextProvider, _adapter, activeModules);
        }
    }

    public GovernanceReport ValidateArchitecture()
    {
        lock (_lock)
            return GovernanceAnalyzer.Analyze(_topology, _adapter);
    }

    public List<CrossWork> GetCrossWorks()
    {
        lock (_lock)
            return _topology.CrossWorks;
    }

    public List<CrossWork> GetCrossWorksForModule(string moduleName)
    {
        lock (_lock)
        {
            return _topology.CrossWorks
                .Where(crossWork => crossWork.Participants.Any(participant =>
                    string.Equals(participant.ModuleName, moduleName, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }
    }

    public void RegisterModule(string discipline, ModuleRegistration module) => _store.RegisterModule(discipline, module);
    public bool UnregisterModule(string name) => _store.UnregisterModule(name);
    public void SaveCrossWork(CrossWorkRegistration crossWork) => _store.SaveCrossWork(crossWork);
    public bool RemoveCrossWork(string crossWorkId) => _store.RemoveCrossWork(crossWorkId);

    public void UpsertDiscipline(string disciplineId, string? displayName, string roleId, List<LayerDefinition> layers)
        => _store.UpsertDiscipline(disciplineId, displayName, roleId, layers);

    public bool RemoveDiscipline(string disciplineId) => _store.RemoveDiscipline(disciplineId);

    public string? GetDisciplineRoleId(string moduleName)
    {
        lock (_lock)
        {
            var node = _topology.Nodes.FirstOrDefault(item =>
                string.Equals(item.Name, moduleName, StringComparison.OrdinalIgnoreCase));
            if (node?.Discipline == null)
                return null;

            var architecture = _store.GetArchitecture();
            return architecture.Disciplines.TryGetValue(node.Discipline, out var definition)
                ? definition.RoleId
                : null;
        }
    }

    public ArchitectureManifest GetArchitecture() => _store.GetArchitecture();
    public ModulesManifest GetModulesManifest() => _store.GetModulesManifest();
    public void ReplaceModulesManifest(ModulesManifest manifest) => _store.ReplaceModulesManifest(manifest);
    public void ReloadManifests() => _store.Reload();
    public void Initialize(string storePath) => _store.Initialize(storePath);

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
