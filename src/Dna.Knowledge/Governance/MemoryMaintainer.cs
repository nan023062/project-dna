using Dna.Knowledge;
using Dna.Memory.Models;
using Dna.Memory.Store;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

namespace Dna.Knowledge.Governance;

/// <summary>
/// 记忆维护器 — 负责知识蒸馏、冲突检测和冗余合并
/// </summary>
internal class MemoryMaintainer
{
    private readonly MemoryStore _memoryStore;
    private readonly ITopoGraphStore _topoGraphStore;
    private readonly ITopoGraphApplicationService _topology;
    private readonly ILogger<MemoryMaintainer> _logger;
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public MemoryMaintainer(
        MemoryStore memoryStore,
        ITopoGraphStore topoGraphStore,
        ITopoGraphApplicationService topology,
        ILogger<MemoryMaintainer> logger)
    {
        _memoryStore = memoryStore;
        _topoGraphStore = topoGraphStore;
        _topology = topology;
        _logger = logger;
    }

    /// <summary>
    /// 执行冲突检测，给互相矛盾的记忆打上 #conflict 标签
    /// </summary>
    public int DetectConflicts()
    {
        var conflictCount = 0;
        
        var activeMemories = _memoryStore.Query(new MemoryFilter
        {
            Freshness = FreshnessFilter.FreshAndAging,
            Limit = 10000
        });

        var identityMemories = activeMemories
            .Where(m => m.Tags.Contains(WellKnownTags.Identity))
            .GroupBy(m => m.NodeId ?? "global")
            .Where(g => g.Count() > 1);

        foreach (var group in identityMemories)
        {
            var sorted = group.OrderByDescending(m => m.CreatedAt).ToList();
            for (int i = 1; i < sorted.Count; i++)
            {
                var memory = sorted[i];
                if (!memory.Tags.Contains("#conflict"))
                {
                    var updatedTags = new List<string>(memory.Tags) { "#conflict" };
                    _memoryStore.UpdateTags(memory.Id, updatedTags);
                    conflictCount++;
                    _logger.LogWarning("检测到模块 [{ModuleId}] 存在冗余的 Identity 记忆 [{MemoryId}]，已标记为 #conflict", group.Key, memory.Id);
                }
            }
        }

        return conflictCount;
    }

    /// <summary>
    /// 归档长期未使用的陈旧记忆
    /// </summary>
    public int ArchiveStaleMemories(TimeSpan staleThreshold)
    {
        var archivedCount = 0;
        var thresholdDate = DateTime.UtcNow - staleThreshold;

        var staleMemories = _memoryStore.Query(new MemoryFilter
        {
            Freshness = FreshnessFilter.IncludeStale,
            Limit = 10000
        }).Where(m => m.Freshness == FreshnessStatus.Stale);

        foreach (var memory in staleMemories)
        {
            var lastActive = memory.LastVerifiedAt ?? memory.CreatedAt;
            if (lastActive < thresholdDate)
            {
                _memoryStore.UpdateFreshness(memory.Id, FreshnessStatus.Archived);
                archivedCount++;
                _logger.LogInformation("记忆 [{Id}] 长期处于 Stale 状态，已自动归档", memory.Id);
            }
        }

        return archivedCount;
    }

    public Task<IReadOnlyList<GovernanceActiveModule>> GetActiveModulesAsync(
        TimeSpan activeWindow,
        int maxModules = 50)
    {
        var threshold = DateTime.UtcNow - NormalizeActiveWindow(activeWindow);
        var modules = GetGovernanceTargets().ToList();
        var activeMemories = _memoryStore.Query(new MemoryFilter
        {
            Freshness = FreshnessFilter.IncludeStale,
            Limit = 5000
        })
        .Where(entry => entry.Freshness != FreshnessStatus.Archived)
        .Where(entry => IsMemoryActiveSince(entry, threshold))
        .ToList();

        var aggregated = new Dictionary<string, GovernanceActiveModuleAccumulator>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in activeMemories)
        {
            var relatedModules = ResolveCandidateModules(entry, modules);
            foreach (var module in relatedModules)
            {
                if (!aggregated.TryGetValue(module.Id, out var item))
                {
                    item = new GovernanceActiveModuleAccumulator(module.Id, module.Name);
                    aggregated[module.Id] = item;
                }

                item.RecentMemoryIds.Add(entry.Id);
                item.Reasons.Add(BuildActiveReason(entry, module));
            }
        }

        var results = aggregated.Values
            .Select(item => new GovernanceActiveModule
            {
                ModuleId = item.ModuleId,
                ModuleName = item.ModuleName,
                RecentMemoryCount = item.RecentMemoryIds.Count,
                RecentMemoryIds = [.. item.RecentMemoryIds],
                Reasons = [.. item.Reasons]
            })
            .OrderByDescending(item => item.RecentMemoryCount)
            .ThenBy(item => item.ModuleName, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(maxModules, 1, 500))
            .ToList();

        return Task.FromResult<IReadOnlyList<GovernanceActiveModule>>(results);
    }

    public async Task<GovernanceScanResult> ScanAsync(GovernanceScanRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var modules = GetGovernanceTargets().ToList();
        var activeModules = request.Scope == GovernanceScopeKind.ActiveChanges
            ? await GetActiveModulesAsync(request.ActiveWindow, request.MaxModules)
            : [];

        var candidates = ResolveGovernanceCandidates(request, modules, activeModules)
            .Take(Math.Clamp(request.MaxModules, 1, 500))
            .ToList();

        var scopeNode = request.Scope is GovernanceScopeKind.Module or GovernanceScopeKind.Subtree
            ? ResolveNode(request.NodeIdOrName?.Trim() ?? string.Empty)
            : null;

        var evolutionReport = await BuildScopedEvolutionReportAsync(candidates, request.MaxSuggestions);
        var architectureReport = FilterGovernanceReport(
            _topology.ValidateArchitecture(),
            candidates,
            _topology.GetCrossWorks());

        return new GovernanceScanResult
        {
            GeneratedAt = DateTime.UtcNow,
            Cadence = request.Cadence,
            Scope = request.Scope,
            ScopeNodeId = scopeNode?.Id,
            ScopeNodeName = scopeNode?.Name,
            ActiveModules = [.. activeModules],
            CandidateModules = candidates,
            ArchitectureReport = architectureReport,
            EvolutionReport = evolutionReport
        };
    }

    public Task<KnowledgeEvolutionReport> EvolveKnowledgeAsync(
        string? nodeIdOrName = null,
        int maxSuggestions = 50)
    {
        var limit = Math.Clamp(maxSuggestions, 1, 200);
        var filterNode = string.IsNullOrWhiteSpace(nodeIdOrName)
            ? null
            : ResolveNode(nodeIdOrName.Trim())
                ?? throw new InvalidOperationException($"节点不存在: {nodeIdOrName}");

        var modules = GetGovernanceTargets().ToList();
        var knowledgeMap = _topoGraphStore.LoadNodeKnowledgeMap();
        var candidates = _memoryStore.Query(new MemoryFilter
        {
            Freshness = FreshnessFilter.FreshAndAging,
            Limit = Math.Clamp(limit * 10, 100, 2000)
        })
        .OrderByDescending(entry => entry.Importance)
        .ThenByDescending(entry => entry.CreatedAt)
        .ToList();

        var suggestions = new List<KnowledgeEvolutionSuggestion>();
        foreach (var entry in candidates)
        {
            var suggestion = TryBuildSessionToMemorySuggestion(entry, filterNode, modules)
                ?? TryBuildMemoryToKnowledgeSuggestion(entry, filterNode, modules, knowledgeMap);
            if (suggestion != null)
                suggestions.Add(suggestion);
        }

        var ordered = suggestions
            .OrderByDescending(item => item.Confidence)
            .ThenByDescending(item => item.TargetLayer)
            .ThenBy(item => item.NodeName ?? item.NodeId ?? item.MemoryId, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToList();

        return Task.FromResult(new KnowledgeEvolutionReport
        {
            GeneratedAt = DateTime.UtcNow,
            FilterNodeId = filterNode?.Id,
            FilterNodeName = filterNode?.Name,
            SessionToMemoryCount = ordered.Count(item =>
                item.CurrentLayer == EvolutionKnowledgeLayer.Session &&
                item.TargetLayer == EvolutionKnowledgeLayer.Memory),
            MemoryToKnowledgeCount = ordered.Count(item =>
                item.CurrentLayer == EvolutionKnowledgeLayer.Memory &&
                item.TargetLayer == EvolutionKnowledgeLayer.Knowledge),
            Suggestions = ordered
        });
    }

    /// <summary>
    /// 按模块压缩记忆：将短期记忆提炼为模块 Identity，并归档已提炼的 Episodic/Working 记忆。
    /// </summary>
    public async Task<KnowledgeCondenseResult> CondenseNodeKnowledgeAsync(
        string nodeIdOrName,
        int maxSourceMemories = 200)
    {
        if (string.IsNullOrWhiteSpace(nodeIdOrName))
            throw new ArgumentException("nodeIdOrName 不能为空。", nameof(nodeIdOrName));

        var node = ResolveNode(nodeIdOrName.Trim())
            ?? throw new InvalidOperationException($"节点不存在: {nodeIdOrName}");

        var source = _memoryStore.Query(new MemoryFilter
        {
            NodeId = node.Id,
            Freshness = FreshnessFilter.FreshAndAging,
            Limit = Math.Clamp(maxSourceMemories, 20, 2000)
        });

        source = source
            .Where(m => !m.Tags.Contains("#condensed", StringComparer.OrdinalIgnoreCase))
            .OrderByDescending(m => m.Importance)
            .ThenByDescending(m => m.CreatedAt)
            .ToList();

        var sessionSource = source
            .Where(m => m.Stage == MemoryStage.ShortTerm)
            .ToList();
        var memorySource = source
            .Where(m => m.Stage != MemoryStage.ShortTerm)
            .ToList();

        if (source.Count == 0)
        {
            return new KnowledgeCondenseResult
            {
                NodeId = node.Id,
                NodeName = node.Name,
                SourceCount = 0,
                SessionSourceCount = 0,
                MemorySourceCount = 0,
                ArchivedCount = 0,
                Summary = "无可压缩记忆"
            };
        }

        var identity = BuildIdentityPayload(node, source);
        var identityRequest = new RememberRequest
        {
            Type = MemoryType.Structural,
            NodeType = node.Type,
            Source = MemorySource.System,
            NodeId = node.Id,
            Disciplines = string.IsNullOrWhiteSpace(node.Discipline) ? [] : [node.Discipline],
            Tags = [WellKnownTags.Identity, "#condensed"],
            Summary = identity.Summary,
            Content = JsonSerializer.Serialize(identity, JsonOpts),
            Importance = 0.9
        };

        var condensed = await _memoryStore.RememberAsync(identityRequest);
        var upgradeTrail = await _memoryStore.RememberAsync(
            BuildCondenseTrailRequest(node, sessionSource, memorySource, condensed));

        // 归档已提炼的短期记忆（避免持续膨胀）
        var archivedCount = 0;
        var archivedMemoryIds = new List<string>();
        foreach (var m in source)
        {
            if (m.Id == condensed.Id) continue;
            if (m.Type is MemoryType.Episodic or MemoryType.Working)
            {
                _memoryStore.UpdateFreshness(m.Id, FreshnessStatus.Archived);
                archivedCount++;
                archivedMemoryIds.Add(m.Id);
            }
        }

        // 历史 identity 仅保留最新一条为 Fresh（其余归档）
        var identities = _memoryStore.Query(new MemoryFilter
        {
            NodeId = node.Id,
            Tags = [WellKnownTags.Identity],
            Freshness = FreshnessFilter.All,
            Limit = 200
        }).OrderByDescending(x => x.CreatedAt).ToList();
        foreach (var old in identities.Skip(1))
        {
            if (old.Freshness != FreshnessStatus.Archived)
            {
                _memoryStore.UpdateFreshness(old.Id, FreshnessStatus.Archived);
                archivedMemoryIds.Add(old.Id);
            }
        }

        var knowledgeView = BuildNodeKnowledgeView(identity, source, condensed, upgradeTrail);
        _topoGraphStore.UpsertNodeKnowledge(node.Id, knowledgeView);

        _logger.LogInformation("模块知识压缩完成: node={Node} source={Source} archived={Archived} identity={IdentityId}",
            node.Name, source.Count, archivedCount, condensed.Id);

        _logger.LogDebug(
            "condense-upgrade-trail: node={Node} sessionSourceIds={SessionIds} memorySourceIds={MemoryIds} archivedIds={ArchivedIds} trail={TrailId}",
            node.Name,
            string.Join(",", sessionSource.Select(m => m.Id)),
            string.Join(",", memorySource.Select(m => m.Id)),
            string.Join(",", archivedMemoryIds.Distinct(StringComparer.OrdinalIgnoreCase)),
            upgradeTrail.Id);

        return new KnowledgeCondenseResult
        {
            NodeId = node.Id,
            NodeName = node.Name,
            SourceCount = source.Count,
            SessionSourceCount = sessionSource.Count,
            MemorySourceCount = memorySource.Count,
            ArchivedCount = archivedCount,
            NewIdentityMemoryId = condensed.Id,
            UpgradeTrailMemoryId = upgradeTrail.Id,
            SessionSourceMemoryIds = [.. sessionSource.Select(m => m.Id)],
            MemorySourceMemoryIds = [.. memorySource.Select(m => m.Id)],
            ArchivedMemoryIds = [.. archivedMemoryIds.Distinct(StringComparer.OrdinalIgnoreCase)],
            Summary = identity.Summary
        };
    }

    /// <summary>
    /// 全量压缩所有模块节点。
    /// </summary>
    public async Task<List<KnowledgeCondenseResult>> CondenseAllNodesAsync(
        int maxSourceMemories = 200)
    {
        var results = new List<KnowledgeCondenseResult>();
        foreach (var node in GetGovernanceTargets())
        {
            try
            {
                var result = await CondenseNodeKnowledgeAsync(node.Id, maxSourceMemories);
                results.Add(result);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "模块知识压缩失败: node={Node}", node.Name);
            }
        }
        return results;
    }

    private GovernanceTargetNode? ResolveNode(string nodeIdOrName)
    {
        var candidates = _topoGraphStore.ResolveNodeIdCandidates(nodeIdOrName, strict: true);
        var modules = _topology.GetManagementSnapshot().Modules;

        foreach (var candidate in candidates)
        {
            var match = modules.FirstOrDefault(module =>
                string.Equals(module.Id, candidate, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(module.Name, candidate, StringComparison.OrdinalIgnoreCase));
            if (match != null)
                return ToGovernanceTarget(match);
        }

        return null;
    }

    private IEnumerable<GovernanceTargetNode> GetGovernanceTargets()
    {
        return _topology.GetManagementSnapshot().Modules.Select(ToGovernanceTarget);
    }

    private async Task<KnowledgeEvolutionReport> BuildScopedEvolutionReportAsync(
        IReadOnlyList<GovernanceCandidateModule> candidates,
        int maxSuggestions)
    {
        var limit = Math.Clamp(maxSuggestions, 1, 200);
        if (candidates.Count == 0)
            return new KnowledgeEvolutionReport { GeneratedAt = DateTime.UtcNow };

        if (candidates.Count == 1 && candidates[0].IsDirectlyActive)
            return await EvolveKnowledgeAsync(candidates[0].ModuleId, limit);

        var suggestions = new Dictionary<string, KnowledgeEvolutionSuggestion>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            var report = await EvolveKnowledgeAsync(candidate.ModuleId, limit);
            foreach (var suggestion in report.Suggestions)
            {
                if (!suggestions.TryGetValue(suggestion.MemoryId, out var existing) ||
                    suggestion.Confidence > existing.Confidence)
                {
                    suggestions[suggestion.MemoryId] = suggestion;
                }
            }
        }

        var ordered = suggestions.Values
            .OrderByDescending(item => item.Confidence)
            .ThenByDescending(item => item.TargetLayer)
            .ThenBy(item => item.NodeName ?? item.NodeId ?? item.MemoryId, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToList();

        return new KnowledgeEvolutionReport
        {
            GeneratedAt = DateTime.UtcNow,
            SessionToMemoryCount = ordered.Count(item =>
                item.CurrentLayer == EvolutionKnowledgeLayer.Session &&
                item.TargetLayer == EvolutionKnowledgeLayer.Memory),
            MemoryToKnowledgeCount = ordered.Count(item =>
                item.CurrentLayer == EvolutionKnowledgeLayer.Memory &&
                item.TargetLayer == EvolutionKnowledgeLayer.Knowledge),
            Suggestions = ordered
        };
    }

    private List<GovernanceCandidateModule> ResolveGovernanceCandidates(
        GovernanceScanRequest request,
        IReadOnlyList<GovernanceTargetNode> modules,
        IReadOnlyList<GovernanceActiveModule> activeModules)
    {
        var selected = new Dictionary<string, GovernanceCandidateModuleBuilder>(StringComparer.OrdinalIgnoreCase);

        switch (request.Scope)
        {
            case GovernanceScopeKind.ActiveChanges:
                foreach (var activeModule in activeModules)
                {
                    AddCandidate(selected, modules, activeModule.ModuleId, isDirectlyActive: true, addedByDependencyExpansion: false, activeModule.Reasons);

                    if (!request.IncludeDirectDependencies)
                        continue;

                    foreach (var dependency in ResolveDirectDependencies(activeModule.ModuleId, modules))
                    {
                        AddCandidate(
                            selected,
                            modules,
                            dependency.Id,
                            isDirectlyActive: false,
                            addedByDependencyExpansion: true,
                            [$"作为活跃模块 {activeModule.ModuleName} 的直接依赖纳入治理"]);
                    }
                }
                break;

            case GovernanceScopeKind.Module:
                AddExplicitScopeCandidate(request, modules, selected);
                break;

            case GovernanceScopeKind.Subtree:
                AddSubtreeScopeCandidates(request, modules, selected);
                break;

            case GovernanceScopeKind.Global:
                foreach (var module in modules)
                {
                    AddCandidate(selected, modules, module.Id, isDirectlyActive: false, addedByDependencyExpansion: false, ["全局治理扫描"]);
                }
                break;
        }

        return selected.Values
            .Select(item => item.Build())
            .OrderByDescending(item => item.IsDirectlyActive)
            .ThenByDescending(item => item.AddedByDependencyExpansion)
            .ThenBy(item => item.ModuleName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void AddExplicitScopeCandidate(
        GovernanceScanRequest request,
        IReadOnlyList<GovernanceTargetNode> modules,
        Dictionary<string, GovernanceCandidateModuleBuilder> selected)
    {
        if (string.IsNullOrWhiteSpace(request.NodeIdOrName))
            throw new InvalidOperationException("模块治理扫描必须提供 NodeIdOrName。");

        var scopeNode = ResolveNode(request.NodeIdOrName.Trim())
            ?? throw new InvalidOperationException($"节点不存在: {request.NodeIdOrName}");

        AddCandidate(selected, modules, scopeNode.Id, isDirectlyActive: true, addedByDependencyExpansion: false, ["指定模块治理"]);

        if (!request.IncludeDirectDependencies)
            return;

        foreach (var dependency in ResolveDirectDependencies(scopeNode.Id, modules))
        {
            AddCandidate(selected, modules, dependency.Id, isDirectlyActive: false, addedByDependencyExpansion: true, [$"作为目标模块 {scopeNode.Name} 的直接依赖纳入治理"]);
        }
    }

    private void AddSubtreeScopeCandidates(
        GovernanceScanRequest request,
        IReadOnlyList<GovernanceTargetNode> modules,
        Dictionary<string, GovernanceCandidateModuleBuilder> selected)
    {
        if (string.IsNullOrWhiteSpace(request.NodeIdOrName))
            throw new InvalidOperationException("子树治理扫描必须提供 NodeIdOrName。");

        var managementModules = _topology.GetManagementSnapshot().Modules;
        var scopeNode = ResolveNode(request.NodeIdOrName.Trim())
            ?? throw new InvalidOperationException($"节点不存在: {request.NodeIdOrName}");

        var subtreeIds = CollectSubtreeIds(scopeNode.Id, managementModules);
        foreach (var subtreeId in subtreeIds)
        {
            AddCandidate(selected, modules, subtreeId, isDirectlyActive: true, addedByDependencyExpansion: false, [$"属于治理子树 {scopeNode.Name}"]);
        }

        if (!request.IncludeDirectDependencies)
            return;

        foreach (var subtreeId in subtreeIds)
        {
            var sourceName = modules.FirstOrDefault(item => string.Equals(item.Id, subtreeId, StringComparison.OrdinalIgnoreCase))?.Name ?? subtreeId;
            foreach (var dependency in ResolveDirectDependencies(subtreeId, modules))
            {
                AddCandidate(selected, modules, dependency.Id, isDirectlyActive: false, addedByDependencyExpansion: true, [$"作为子树模块 {sourceName} 的直接依赖纳入治理"]);
            }
        }
    }

    private static HashSet<string> CollectSubtreeIds(string rootId, IReadOnlyList<TopologyModuleDefinition> modules)
    {
        var childrenMap = modules
            .Where(module => !string.IsNullOrWhiteSpace(module.ParentModuleId))
            .GroupBy(module => module.ParentModuleId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Id).ToList(), StringComparer.OrdinalIgnoreCase);

        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { rootId };
        var queue = new Queue<string>();
        queue.Enqueue(rootId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!childrenMap.TryGetValue(current, out var children))
                continue;

            foreach (var child in children)
            {
                if (results.Add(child))
                    queue.Enqueue(child);
            }
        }

        return results;
    }

    private static string BuildActiveReason(MemoryEntry entry, GovernanceTargetNode module)
    {
        if (string.Equals(entry.NodeId, module.Id, StringComparison.OrdinalIgnoreCase))
            return "近期记忆直接绑定该模块";

        if (entry.Tags.Contains(WellKnownTags.ActiveTask, StringComparer.OrdinalIgnoreCase))
            return "近期活跃任务关联到该模块";

        return "近期记忆关联到该模块";
    }

    private static TimeSpan NormalizeActiveWindow(TimeSpan activeWindow)
        => activeWindow <= TimeSpan.Zero ? TimeSpan.FromDays(1) : activeWindow;

    private static bool IsMemoryActiveSince(MemoryEntry entry, DateTime threshold)
    {
        var lastActive = entry.LastVerifiedAt ?? entry.CreatedAt;
        return lastActive >= threshold;
    }

    private static GovernanceReport FilterGovernanceReport(
        GovernanceReport source,
        IReadOnlyList<GovernanceCandidateModule> candidates,
        IReadOnlyList<CrossWork> crossWorks)
    {
        if (candidates.Count == 0)
            return new GovernanceReport();

        var moduleIds = new HashSet<string>(candidates.Select(item => item.ModuleId), StringComparer.OrdinalIgnoreCase);
        var moduleNames = new HashSet<string>(candidates.Select(item => item.ModuleName), StringComparer.OrdinalIgnoreCase);
        var crossWorkMap = crossWorks.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);

        return new GovernanceReport
        {
            CycleSuggestions = source.CycleSuggestions
                .Where(item => item.CycleMembers.Any(member => moduleNames.Contains(member) || moduleIds.Contains(member)))
                .ToList(),
            OrphanNodes = source.OrphanNodes
                .Where(item => moduleIds.Contains(item.Id) || moduleNames.Contains(item.Name))
                .ToList(),
            CrossWorkIssues = source.CrossWorkIssues
                .Where(item =>
                    crossWorkMap.TryGetValue(item.CrossWorkId, out var crossWork) &&
                    crossWork.Participants.Any(participant => moduleNames.Contains(participant.ModuleName)))
                .ToList(),
            DependencyDrifts = source.DependencyDrifts
                .Where(item => moduleNames.Contains(item.ModuleName) || moduleIds.Contains(item.ModuleName))
                .ToList(),
            KeyNodeWarnings = source.KeyNodeWarnings
                .Where(item => moduleNames.Contains(item.NodeName) || moduleIds.Contains(item.NodeName))
                .ToList()
        };
    }

    private static IReadOnlyList<GovernanceTargetNode> ResolveDirectDependencies(
        string moduleId,
        IReadOnlyList<GovernanceTargetNode> modules)
    {
        var managementModules = modules.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        var resolved = new List<GovernanceTargetNode>();
        var source = managementModules.GetValueOrDefault(moduleId);
        if (source == null)
            return resolved;

        foreach (var dependency in source.Dependencies)
        {
            var match = modules.FirstOrDefault(item =>
                string.Equals(item.Id, dependency, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.Name, dependency, StringComparison.OrdinalIgnoreCase));
            if (match != null)
                resolved.Add(match);
        }

        return resolved;
    }

    private static void AddCandidate(
        IDictionary<string, GovernanceCandidateModuleBuilder> selected,
        IReadOnlyList<GovernanceTargetNode> modules,
        string moduleIdOrName,
        bool isDirectlyActive,
        bool addedByDependencyExpansion,
        IEnumerable<string> reasons)
    {
        var module = modules.FirstOrDefault(item =>
            string.Equals(item.Id, moduleIdOrName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(item.Name, moduleIdOrName, StringComparison.OrdinalIgnoreCase));
        if (module == null)
            return;

        if (!selected.TryGetValue(module.Id, out var builder))
        {
            builder = new GovernanceCandidateModuleBuilder(module.Id, module.Name);
            selected[module.Id] = builder;
        }

        builder.IsDirectlyActive |= isDirectlyActive;
        builder.AddedByDependencyExpansion |= addedByDependencyExpansion;
        builder.Reasons.UnionWith(reasons.Where(reason => !string.IsNullOrWhiteSpace(reason)));
    }

    private KnowledgeEvolutionSuggestion? TryBuildSessionToMemorySuggestion(
        MemoryEntry entry,
        GovernanceTargetNode? filterNode,
        IReadOnlyList<GovernanceTargetNode> modules)
    {
        if (entry.Stage != MemoryStage.ShortTerm)
            return null;

        if (entry.Freshness == FreshnessStatus.Archived ||
            entry.Tags.Contains("#condensed", StringComparer.OrdinalIgnoreCase))
            return null;

        var relatedModules = ResolveCandidateModules(entry, modules);
        if (relatedModules.Count == 0 || !MatchesFilter(entry, relatedModules, filterNode))
            return null;

        var looksStable =
            entry.Type is MemoryType.Structural or MemoryType.Procedural ||
            entry.Importance >= 0.7 ||
            entry.Tags.Contains(WellKnownTags.ActiveTask, StringComparer.OrdinalIgnoreCase);
        if (!looksStable)
            return null;

        var reasons = new List<string>();
        var confidence = 0.58;

        if (!string.IsNullOrWhiteSpace(entry.NodeId))
        {
            reasons.Add("已绑定具体模块");
            confidence += 0.12;
        }

        if (entry.Tags.Contains(WellKnownTags.ActiveTask, StringComparer.OrdinalIgnoreCase))
        {
            reasons.Add("包含可延续的任务上下文");
            confidence += 0.1;
        }

        if (entry.Type is MemoryType.Structural or MemoryType.Procedural)
        {
            reasons.Add("内容已接近稳定规则或约定");
            confidence += 0.12;
        }

        if (entry.Importance >= 0.7)
        {
            reasons.Add("重要度较高");
            confidence += 0.08;
        }

        return new KnowledgeEvolutionSuggestion
        {
            MemoryId = entry.Id,
            NodeId = entry.NodeId,
            NodeName = relatedModules[0].Name,
            Type = entry.Type,
            Stage = entry.Stage,
            CurrentLayer = EvolutionKnowledgeLayer.Session,
            TargetLayer = EvolutionKnowledgeLayer.Memory,
            Summary = entry.Summary,
            Reason = string.Join("；", reasons),
            Confidence = Math.Round(Math.Min(confidence, 0.95), 3),
            Tags = [.. entry.Tags],
            CandidateModuleIds = [.. relatedModules.Select(module => module.Id)],
            CandidateModuleNames = [.. relatedModules.Select(module => module.Name)]
        };
    }

    private KnowledgeEvolutionSuggestion? TryBuildMemoryToKnowledgeSuggestion(
        MemoryEntry entry,
        GovernanceTargetNode? filterNode,
        IReadOnlyList<GovernanceTargetNode> modules,
        IReadOnlyDictionary<string, NodeKnowledge> knowledgeMap)
    {
        if (entry.Stage != MemoryStage.LongTerm ||
            string.IsNullOrWhiteSpace(entry.NodeId) ||
            entry.Freshness == FreshnessStatus.Archived ||
            entry.Tags.Contains("#upgrade-trail", StringComparer.OrdinalIgnoreCase) ||
            entry.Tags.Contains("#conflict", StringComparer.OrdinalIgnoreCase))
            return null;

        var relatedModules = ResolveCandidateModules(entry, modules);
        if (relatedModules.Count == 0 || !MatchesFilter(entry, relatedModules, filterNode))
            return null;

        var primaryNodeId = relatedModules[0].Id;
        if (knowledgeMap.TryGetValue(primaryNodeId, out var knowledge) &&
            knowledge.MemoryIds.Contains(entry.Id, StringComparer.OrdinalIgnoreCase))
            return null;

        var isKnowledgeCandidate =
            entry.Tags.Contains(WellKnownTags.Identity, StringComparer.OrdinalIgnoreCase) ||
            entry.Tags.Contains(WellKnownTags.Lesson, StringComparer.OrdinalIgnoreCase) ||
            entry.Tags.Contains("#decision", StringComparer.OrdinalIgnoreCase) ||
            entry.Tags.Contains("#convention", StringComparer.OrdinalIgnoreCase) ||
            entry.Type is MemoryType.Structural or MemoryType.Procedural ||
            entry.Importance >= 0.8;
        if (!isKnowledgeCandidate)
            return null;

        var reasons = new List<string> { "已是长期记忆" };
        var confidence = 0.62;

        if (entry.Tags.Contains(WellKnownTags.Identity, StringComparer.OrdinalIgnoreCase))
        {
            reasons.Add("包含模块身份信息");
            confidence += 0.16;
        }

        if (entry.Tags.Contains(WellKnownTags.Lesson, StringComparer.OrdinalIgnoreCase))
        {
            reasons.Add("包含可复用教训");
            confidence += 0.08;
        }

        if (entry.Type is MemoryType.Structural or MemoryType.Procedural)
        {
            reasons.Add("语义接近结构化知识");
            confidence += 0.08;
        }

        if (entry.Importance >= 0.8)
        {
            reasons.Add("重要度已达到治理阈值");
            confidence += 0.06;
        }

        return new KnowledgeEvolutionSuggestion
        {
            MemoryId = entry.Id,
            NodeId = entry.NodeId,
            NodeName = relatedModules[0].Name,
            Type = entry.Type,
            Stage = entry.Stage,
            CurrentLayer = EvolutionKnowledgeLayer.Memory,
            TargetLayer = EvolutionKnowledgeLayer.Knowledge,
            Summary = entry.Summary,
            Reason = string.Join("；", reasons),
            Confidence = Math.Round(Math.Min(confidence, 0.97), 3),
            Tags = [.. entry.Tags],
            CandidateModuleIds = [.. relatedModules.Select(module => module.Id)],
            CandidateModuleNames = [.. relatedModules.Select(module => module.Name)]
        };
    }

    private static bool MatchesFilter(
        MemoryEntry entry,
        IReadOnlyList<GovernanceTargetNode> relatedModules,
        GovernanceTargetNode? filterNode)
    {
        if (filterNode == null)
            return true;

        if (string.Equals(entry.NodeId, filterNode.Id, StringComparison.OrdinalIgnoreCase))
            return true;

        return relatedModules.Any(module =>
            string.Equals(module.Id, filterNode.Id, StringComparison.OrdinalIgnoreCase));
    }

    private static List<GovernanceTargetNode> ResolveCandidateModules(
        MemoryEntry entry,
        IReadOnlyList<GovernanceTargetNode> modules)
    {
        var results = new List<GovernanceTargetNode>();

        if (!string.IsNullOrWhiteSpace(entry.NodeId))
        {
            var direct = modules.FirstOrDefault(module =>
                string.Equals(module.Id, entry.NodeId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(module.Name, entry.NodeId, StringComparison.OrdinalIgnoreCase));
            if (direct != null)
                results.Add(direct);
        }

        if (entry.Tags.Contains(WellKnownTags.ActiveTask, StringComparer.OrdinalIgnoreCase))
        {
            var payload = ParseActiveTaskPayload(entry);
            foreach (var related in payload?.RelatedModules ?? [])
            {
                var match = modules.FirstOrDefault(module =>
                    string.Equals(module.Id, related, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(module.Name, related, StringComparison.OrdinalIgnoreCase));
                if (match != null &&
                    results.All(item => !string.Equals(item.Id, match.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    results.Add(match);
                }
            }
        }

        return results;
    }

    private static GovernanceTargetNode ToGovernanceTarget(TopologyModuleDefinition module)
    {
        var nodeType = module.IsCrossWorkModule
            ? NodeType.Team
            : module.Layer >= 3
                ? NodeType.Team
                : NodeType.Technical;

        return new GovernanceTargetNode(
            module.Id,
            module.Name,
            nodeType,
            module.Discipline,
            module.Summary,
            module.Boundary,
            module.Dependencies ?? []);
    }

    private static IdentityPayload BuildIdentityPayload(GovernanceTargetNode node, List<MemoryEntry> source)
    {
        var previousIdentity = source
            .Where(m => m.Tags.Contains(WellKnownTags.Identity, StringComparer.OrdinalIgnoreCase))
            .OrderByDescending(m => m.CreatedAt)
            .Select(ParseIdentityPayload)
            .FirstOrDefault(p => p != null);

        var lessons = source
            .Where(m => m.Tags.Contains(WellKnownTags.Lesson, StringComparer.OrdinalIgnoreCase))
            .Select(ParseLessonPayload)
            .Where(p => p != null)
            .Take(5)
            .ToList();

        var activeTasks = source
            .Where(m => m.Tags.Contains(WellKnownTags.ActiveTask, StringComparer.OrdinalIgnoreCase))
            .Select(ParseActiveTaskPayload)
            .Where(p => p != null)
            .Take(5)
            .ToList();

        var summaryBase = previousIdentity?.Summary
            ?? node.Summary
            ?? $"模块 {node.Name} 的长期知识摘要";

        var detail = new StringBuilder();
        if (lessons.Count > 0)
        {
            detail.AppendLine("近期教训:");
            foreach (var l in lessons)
                detail.AppendLine($"- {l!.Title}: {l.Context}");
        }
        if (activeTasks.Count > 0)
        {
            if (detail.Length > 0) detail.AppendLine();
            detail.AppendLine("当前任务:");
            foreach (var t in activeTasks)
                detail.AppendLine($"- {t!.Task} ({t.Status ?? "unknown"})");
        }

        var keywords = source
            .SelectMany(m => m.Tags)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();

        return new IdentityPayload
        {
            Summary = summaryBase,
            Contract = previousIdentity?.Contract ?? node.Contract,
            Description = detail.Length == 0 ? null : detail.ToString().Trim(),
            Keywords = keywords
        };
    }

    private static IdentityPayload? ParseIdentityPayload(MemoryEntry entry)
    {
        try
        {
            return JsonSerializer.Deserialize<IdentityPayload>(entry.Content, JsonOpts);
        }
        catch
        {
            return null;
        }
    }

    private static LessonPayload? ParseLessonPayload(MemoryEntry entry)
    {
        try
        {
            return JsonSerializer.Deserialize<LessonPayload>(entry.Content, JsonOpts);
        }
        catch
        {
            return new LessonPayload
            {
                Title = entry.Summary ?? "untitled",
                Context = entry.Content
            };
        }
    }

    private static ActiveTaskPayload? ParseActiveTaskPayload(MemoryEntry entry)
    {
        try
        {
            return JsonSerializer.Deserialize<ActiveTaskPayload>(entry.Content, JsonOpts);
        }
        catch
        {
            return new ActiveTaskPayload
            {
                Task = entry.Summary ?? "unknown",
                Notes = entry.Content
            };
        }
    }

    private static RememberRequest BuildCondenseTrailRequest(
        GovernanceTargetNode node,
        List<MemoryEntry> sessionSource,
        List<MemoryEntry> memorySource,
        MemoryEntry condensed)
    {
        return new RememberRequest
        {
            Type = MemoryType.Episodic,
            NodeType = node.Type,
            Source = MemorySource.System,
            NodeId = node.Id,
            Disciplines = string.IsNullOrWhiteSpace(node.Discipline) ? [] : [node.Discipline],
            Tags = ["#condensed", "#completed-task", "#upgrade-trail"],
            Stage = MemoryStage.LongTerm,
            Summary = $"Condense trail for {node.Name}",
            Content = BuildCondenseTrailContent(node, sessionSource, memorySource, condensed),
            Importance = 0.7
        };
    }

    private static string BuildCondenseTrailContent(
        GovernanceTargetNode node,
        List<MemoryEntry> sessionSource,
        List<MemoryEntry> memorySource,
        MemoryEntry condensed)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Condense Trail: {node.Name}");
        sb.AppendLine();
        sb.AppendLine($"Generated identity memory `{condensed.Id}` from {sessionSource.Count + memorySource.Count} source memories.");
        sb.AppendLine();
        sb.AppendLine("## Session Sources");
        if (sessionSource.Count == 0)
        {
            sb.AppendLine("- None");
        }
        else
        {
            foreach (var entry in sessionSource)
                sb.AppendLine($"- [{entry.Id}] {entry.Summary ?? entry.Content}");
        }

        sb.AppendLine();
        sb.AppendLine("## Long-Term Sources");
        if (memorySource.Count == 0)
        {
            sb.AppendLine("- None");
        }
        else
        {
            foreach (var entry in memorySource)
                sb.AppendLine($"- [{entry.Id}] {entry.Summary ?? entry.Content}");
        }

        return sb.ToString().Trim();
    }

    private static NodeKnowledge BuildNodeKnowledgeView(
        IdentityPayload identity,
        List<MemoryEntry> source,
        MemoryEntry condensed,
        MemoryEntry upgradeTrail)
    {
        var lessons = source
            .Where(m => m.Tags.Contains(WellKnownTags.Lesson, StringComparer.OrdinalIgnoreCase))
            .Select(ParseLessonPayload)
            .Where(p => p != null)
            .Take(10)
            .Select(p => new LessonSummary
            {
                Title = p!.Title,
                Severity = p.Severity,
                Resolution = p.Resolution
            })
            .ToList();

        var activeTasks = source
            .Where(m => m.Tags.Contains(WellKnownTags.ActiveTask, StringComparer.OrdinalIgnoreCase))
            .Select(ParseActiveTaskPayload)
            .Where(p => p != null && !string.IsNullOrWhiteSpace(p.Task))
            .Take(10)
            .Select(p => p!.Task)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var facts = source
            .OrderByDescending(m => m.Importance)
            .ThenByDescending(m => m.CreatedAt)
            .Select(m => m.Summary)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Take(20)
            .Select(s => s!)
            .ToList();

        var memoryIds = source
            .Select(m => m.Id)
            .Concat([condensed.Id, upgradeTrail.Id])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(200)
            .ToList();

        return new NodeKnowledge
        {
            Identity = identity.Summary,
            Lessons = lessons,
            ActiveTasks = activeTasks,
            Facts = facts,
            TotalMemoryCount = source.Count,
            IdentityMemoryId = condensed.Id,
            UpgradeTrailMemoryId = upgradeTrail.Id,
            MemoryIds = memoryIds
        };
    }

    private sealed record GovernanceTargetNode(
        string Id,
        string Name,
        NodeType Type,
        string? Discipline,
        string? Summary,
        string? Contract,
        IReadOnlyList<string> Dependencies);

    private sealed class GovernanceActiveModuleAccumulator
    {
        public GovernanceActiveModuleAccumulator(string moduleId, string moduleName)
        {
            ModuleId = moduleId;
            ModuleName = moduleName;
        }

        public string ModuleId { get; }
        public string ModuleName { get; }
        public HashSet<string> RecentMemoryIds { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Reasons { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class GovernanceCandidateModuleBuilder
    {
        public GovernanceCandidateModuleBuilder(string moduleId, string moduleName)
        {
            ModuleId = moduleId;
            ModuleName = moduleName;
        }

        public string ModuleId { get; }
        public string ModuleName { get; }
        public bool IsDirectlyActive { get; set; }
        public bool AddedByDependencyExpansion { get; set; }
        public HashSet<string> Reasons { get; } = new(StringComparer.OrdinalIgnoreCase);

        public GovernanceCandidateModule Build()
        {
            return new GovernanceCandidateModule
            {
                ModuleId = ModuleId,
                ModuleName = ModuleName,
                IsDirectlyActive = IsDirectlyActive,
                AddedByDependencyExpansion = AddedByDependencyExpansion,
                Reasons = [.. Reasons]
            };
        }
    }
}
