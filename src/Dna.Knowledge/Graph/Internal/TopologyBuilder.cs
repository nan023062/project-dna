using System.Text.Json;
using Dna.Knowledge.Models;
using Dna.Knowledge.Project.Models;
using Dna.Memory.Models;
using Dna.Memory.Store;

namespace Dna.Knowledge;

/// <summary>
/// 拓扑构建器 — 从 MemoryStore 数据构建模块依赖图。
/// 全部数据来自 MemoryStore（ModulesManifest + Structural 记忆），不访问磁盘。
/// </summary>
internal static class TopologyBuilder
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private static readonly List<string> IdentityTags = [WellKnownTags.Identity];

    internal static TopologySnapshot Build(MemoryStore store)
    {
        var arch = store.GetArchitecture();
        var manifest = store.GetModulesManifest();
        var computed = store.GetComputedManifest();
        var nodes = BuildNodes(arch, manifest, computed, store);
        var edges = BuildEdges(nodes);
        var (depMap, rdepMap) = BuildDepMaps(edges);
        var crossWorks = ExtractCrossWorks(manifest);

        return new TopologySnapshot
        {
            Nodes = nodes,
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
            if (!subset.Contains(edge.From) || !subset.Contains(edge.To)) continue;
            adj[edge.To].Add(edge.From);
            inDegree[edge.From] = inDegree.GetValueOrDefault(edge.From) + 1;
        }

        var queue = new Queue<string>(inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));
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

        if (ordered.Count < subset.Count)
        {
            var stuck = subset.Except(ordered).ToList();
            return new ExecutionPlan
            {
                OrderedModules = ordered,
                HasCycle = true,
                CycleDescription = $"Circular dependency detected among: {string.Join(", ", stuck)}"
            };
        }

        return new ExecutionPlan { OrderedModules = ordered };
    }

    // ═══════════════════════════════════════════
    //  节点构建
    // ═══════════════════════════════════════════

    private static List<KnowledgeNode> BuildNodes(ArchitectureManifest arch, ModulesManifest manifest, ComputedManifest computed, MemoryStore store)
    {
        var nodes = new List<KnowledgeNode>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var nodeKnowledgeMap = store.LoadNodeKnowledgeMap();

        foreach (var (discipline, modules) in manifest.Disciplines)
        {
            foreach (var reg in modules)
            {
                if (!seenNames.Add(reg.Name))
                    continue;

                var node = new KnowledgeNode
                {
                    Id = reg.Id,
                    Name = reg.Name,
                    Type = reg.IsCrossWorkModule ? NodeType.Team : NodeType.Technical,
                    RelativePath = reg.Path,
                    Maintainer = reg.Maintainer,
                    Summary = reg.Summary,
                    Boundary = reg.Boundary,
                    PublicApi = reg.PublicApi,
                    Constraints = reg.Constraints,
                    Metadata = reg.Metadata,

                    Discipline = discipline,
                    IsCrossWorkModule = reg.IsCrossWorkModule,
                    Dependencies = reg.IsCrossWorkModule ? [] : reg.Dependencies.ToList(),
                    ComputedDependencies = reg.IsCrossWorkModule ? [] : computed.ModuleDependencies.GetValueOrDefault(reg.Name, []),
                    Knowledge = nodeKnowledgeMap.GetValueOrDefault(reg.Id, new NodeKnowledge())
                };

                if (string.IsNullOrWhiteSpace(node.Summary) && !string.IsNullOrWhiteSpace(node.Knowledge.Identity))
                    node.Summary = node.Knowledge.Identity;

                var identityEntries = store.Query(new MemoryFilter
                {
                    Tags = IdentityTags,
                    NodeId = reg.Id,
                    Freshness = FreshnessFilter.All,
                    Limit = 1
                });
                var identity = identityEntries.FirstOrDefault();
                if (identity != null)
                {
                    node.Summary = identity.Summary;
                    TryApplyIdentityPayload(identity.Content, node);
                    if (string.IsNullOrWhiteSpace(node.Knowledge.Identity))
                        node.Knowledge.Identity = identity.Summary;
                }

                nodes.Add(node);
            }
        }

        return nodes;
    }

    // ═══════════════════════════════════════════
    //  边构建
    // ═══════════════════════════════════════════

    private static List<KnowledgeEdge> BuildEdges(List<KnowledgeNode> nodes)
    {
        var edges = new List<KnowledgeEdge>();
        var nodeMap = new HashSet<string>(nodes.Select(n => n.Name), StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var node in nodes)
        {
            foreach (var dep in node.ComputedDependencies)
            {
                if (!nodeMap.Contains(dep)) continue;
                var key = $"{node.Name}→{dep}";
                if (seen.Add(key))
                    edges.Add(new KnowledgeEdge { From = node.Name, To = dep, IsComputed = true });
            }

            foreach (var dep in node.Dependencies)
            {
                if (!nodeMap.Contains(dep)) continue;
                var key = $"{node.Name}→{dep}";
                if (seen.Add(key))
                    edges.Add(new KnowledgeEdge { From = node.Name, To = dep, IsComputed = false });
            }
        }

        return edges;
    }

    // ═══════════════════════════════════════════
    //  CrossWork 提取（两个来源合并）
    // ═══════════════════════════════════════════

    private static List<CrossWork> ExtractCrossWorks(ModulesManifest manifest)
    {
        var result = new List<CrossWork>();

        foreach (var cw in manifest.CrossWorks)
        {
            if (string.IsNullOrWhiteSpace(cw.Name) || cw.Participants.Count < 2)
                continue;
            result.Add(new CrossWork
            {
                Id = cw.Id,
                Name = cw.Name,
                Description = cw.Description,
                Feature = cw.Feature,
                Participants = cw.Participants
                    .Select(p => new CrossWorkParticipant
                    {
                        ModuleName = p.ModuleName,
                        Role = p.Role,
                        Contract = p.Contract,
                        ContractType = p.ContractType,
                        Deliverable = p.Deliverable
                    })
                    .ToList()
            });
        }

        foreach (var (_, modules) in manifest.Disciplines)
        {
            foreach (var reg in modules.Where(m => m.IsCrossWorkModule && m.Participants.Count > 0))
            {
                result.Add(new CrossWork
                {
                    Id = reg.Id,
                    Name = reg.Name,
                    Description = null,
                    Feature = null,
                    Participants = reg.Participants
                        .Select(p => new CrossWorkParticipant
                        {
                            ModuleName = p.ModuleName,
                            Role = p.Role,
                            Contract = p.Contract,
                            ContractType = p.ContractType,
                            Deliverable = p.Deliverable
                        })
                        .ToList()
                });
            }
        }

        return result;
    }

    // ═══════════════════════════════════════════
    //  依赖映射构建
    // ═══════════════════════════════════════════

    private static (Dictionary<string, List<string>> DepMap, Dictionary<string, List<string>> RdepMap) BuildDepMaps(List<KnowledgeEdge> edges)
    {
        var dep = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var rdep = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var e in edges)
        {
            if (!dep.TryGetValue(e.From, out var dList)) { dList = []; dep[e.From] = dList; }
            dList.Add(e.To);
            if (!rdep.TryGetValue(e.To, out var rList)) { rList = []; rdep[e.To] = rList; }
            rList.Add(e.From);
        }

        return (dep, rdep);
    }

    // ═══════════════════════════════════════════
    //  工具方法
    // ═══════════════════════════════════════════

    internal static Dictionary<string, KnowledgeNode> SafeNodeMap(List<KnowledgeNode> nodes)
    {
        var map = new Dictionary<string, KnowledgeNode>(nodes.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var n in nodes)
            map.TryAdd(n.Name, n);
        return map;
    }

    private static void TryApplyIdentityPayload(string content, KnowledgeNode node)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<IdentityPayload>(content, JsonOpts);
            if (payload == null) return;

            if (!string.IsNullOrWhiteSpace(payload.Summary))
                node.Summary = payload.Summary;

            if (payload.Keywords.Count > 0)
                node.Keywords = payload.Keywords
                    .Where(k => !string.IsNullOrWhiteSpace(k))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
        }
        catch
        {
            // identity content is expected to be JSON; ignore malformed payload
        }
    }
}
