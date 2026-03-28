namespace Dna.Knowledge;

/// <summary>
/// 架构治理分析器 — 纯数据分析，只操作 TopologySnapshot，不碰 MemoryStore。
/// 循环依赖为重组建议（而非违规），依赖可自由跨组织边界。
/// </summary>
internal static class GovernanceAnalyzer
{
    private const int KeyNodeThreshold = 5;

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
        foreach (var n in nodes)
            map.TryAdd(n.Name, n);
        return map;
    }

    /// <summary>Tarjan SCC 检测所有环路（size > 1 的强连通分量），返回重组建议</summary>
    private static List<CycleSuggestion> DetectAllCycles(TopologySnapshot topo)
    {
        var adj = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in topo.Nodes) adj[n.Name] = [];
        foreach (var e in topo.Edges)
            adj.GetValueOrDefault(e.From, []).Add(e.To);

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
                Message = $"循环依赖: {string.Join(" → ", scc)} → {scc[0]}",
                Suggestion = "建议提取公共接口到独立模块，或改为 CrossWork 协作以打断循环"
            })
            .ToList();
    }

    private static void Tarjan(
        string v,
        Dictionary<string, List<string>> adj,
        ref int index,
        Stack<string> stack,
        HashSet<string> onStack,
        Dictionary<string, int> indices,
        Dictionary<string, int> lowLinks,
        List<List<string>> sccs)
    {
        indices[v] = index;
        lowLinks[v] = index;
        index++;
        stack.Push(v);
        onStack.Add(v);

        foreach (var w in adj.GetValueOrDefault(v, []))
        {
            if (!indices.ContainsKey(w))
            {
                Tarjan(w, adj, ref index, stack, onStack, indices, lowLinks, sccs);
                lowLinks[v] = Math.Min(lowLinks[v], lowLinks[w]);
            }
            else if (onStack.Contains(w))
            {
                lowLinks[v] = Math.Min(lowLinks[v], indices[w]);
            }
        }

        if (lowLinks[v] == indices[v])
        {
            var scc = new List<string>();
            string w;
            do
            {
                w = stack.Pop();
                onStack.Remove(w);
                scc.Add(w);
            } while (!string.Equals(w, v, StringComparison.OrdinalIgnoreCase));

            sccs.Add(scc);
        }
    }

    private static List<KnowledgeNode> DetectOrphanNodes(TopologySnapshot topo)
    {
        var connected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in topo.Edges)
        {
            connected.Add(e.From);
            connected.Add(e.To);
        }
        foreach (var cw in topo.CrossWorks)
        {
            foreach (var p in cw.Participants)
                connected.Add(p.ModuleName);
        }

        return topo.Nodes
            .Where(n => !n.IsCrossWorkModule && !connected.Contains(n.Name))
            .ToList();
    }

    private static List<CrossWorkIssue> ValidateCrossWorks(TopologySnapshot topo, IProjectAdapter? adapter)
    {
        var issues = new List<CrossWorkIssue>();
        var nodeMap = SafeNodeMap(topo.Nodes);
        var participantNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var cw in topo.CrossWorks)
        {
            participantNames.Clear();
            foreach (var p in cw.Participants)
            {
                participantNames.Add(p.ModuleName);

                if (!nodeMap.TryGetValue(p.ModuleName, out var node))
                {
                    issues.Add(new CrossWorkIssue
                    {
                        CrossWorkId = cw.Id,
                        CrossWorkName = cw.Name,
                        Message = $"参与方 '{p.ModuleName}' 不存在于拓扑中"
                    });
                    continue;
                }

                if (string.IsNullOrWhiteSpace(p.Contract) && string.IsNullOrWhiteSpace(p.Deliverable))
                {
                    issues.Add(new CrossWorkIssue
                    {
                        CrossWorkId = cw.Id,
                        CrossWorkName = cw.Name,
                        Message = $"参与方 '{p.ModuleName}' 未声明 Contract 或 Deliverable"
                    });
                }

                if (adapter != null && !string.IsNullOrWhiteSpace(p.ContractType))
                {
                    var result = adapter.ValidateContract(p, node);
                    if (!result.IsValid)
                    {
                        issues.Add(new CrossWorkIssue
                        {
                            CrossWorkId = cw.Id,
                            CrossWorkName = cw.Name,
                            Message = $"参与方 '{p.ModuleName}' 契约校验失败: {string.Join(", ", result.Errors)}"
                        });
                    }
                }
            }

            var hasDirectDep = topo.Edges.Any(e =>
                participantNames.Contains(e.From) && participantNames.Contains(e.To));
            if (hasDirectDep)
            {
                issues.Add(new CrossWorkIssue
                {
                    CrossWorkId = cw.Id,
                    CrossWorkName = cw.Name,
                    Message = "参与方之间存在直接依赖 Edge — CrossWork 应替代直接依赖，请移除 Dependencies"
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

            foreach (var dep in node.Dependencies)
                declared.Add(dep);
            foreach (var dep in node.ComputedDependencies)
                computed.Add(dep);

            if (declared.SetEquals(computed))
                continue;

            foreach (var dep in declared)
            {
                if (!computed.Contains(dep))
                    declaredOnly.Add(dep);
            }
            foreach (var dep in computed)
            {
                if (!declared.Contains(dep))
                    computedOnly.Add(dep);
            }
            declaredOnly.Sort(StringComparer.OrdinalIgnoreCase);
            computedOnly.Sort(StringComparer.OrdinalIgnoreCase);

            var parts = new List<string>();
            if (declaredOnly.Count > 0)
                parts.Add($"仅声明: [{string.Join(", ", declaredOnly)}]");
            if (computedOnly.Count > 0)
                parts.Add($"仅计算: [{string.Join(", ", computedOnly)}]");

            issues.Add(new DependencyDriftIssue
            {
                ModuleName = node.Name,
                Message = $"依赖偏差 — {string.Join("; ", parts)}",
                DeclaredOnly = [.. declaredOnly],
                ComputedOnly = [.. computedOnly],
                Suggestion = declaredOnly.Count > 0 && computedOnly.Count > 0
                    ? "同步 modules.json 中的 Dependencies 与 ComputedDependencies"
                    : declaredOnly.Count > 0
                        ? "声明了但实际未使用，考虑从 modules.json 移除多余依赖"
                        : "实际存在但未声明，考虑在 modules.json 中补充声明"
            });
        }

        return issues;
    }

    private static List<KeyNodeWarning> DetectKeyNodes(TopologySnapshot topo)
    {
        var warnings = new List<KeyNodeWarning>();

        foreach (var (nodeName, dependents) in topo.RdepMap)
        {
            if (dependents.Count >= KeyNodeThreshold)
            {
                warnings.Add(new KeyNodeWarning
                {
                    NodeName = nodeName,
                    DependentCount = dependents.Count,
                    Message = $"关键节点: {nodeName} 被 {dependents.Count} 个模块依赖，变更需谨慎评估影响范围"
                });
            }
        }

        return warnings;
    }
}
