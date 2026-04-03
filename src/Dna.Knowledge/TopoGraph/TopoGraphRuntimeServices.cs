using System.Text.Json;

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
}
