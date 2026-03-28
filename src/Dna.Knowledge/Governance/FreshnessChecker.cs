using System.IO;
using Dna.Memory.Models;
using Dna.Memory.Store;
using Microsoft.Extensions.Logging;

namespace Dna.Knowledge.Governance;

/// <summary>
/// 记忆鲜活度检查器 — 扫描记忆库，根据时间、路径变更等因素自动降级（Decay）过时的记忆。
/// </summary>
internal class FreshnessChecker
{
    private readonly MemoryStore _store;
    private readonly ILogger<FreshnessChecker> _logger;

    public FreshnessChecker(MemoryStore store, ILogger<FreshnessChecker> logger)
    {
        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// 执行全量鲜活度检查，返回被降级的记忆数量。
    /// </summary>
    public int CheckAll(TopologySnapshot topology)
    {
        var decayedCount = 0;
        var projectRoot = _store.ProjectRoot;
        if (string.IsNullOrEmpty(projectRoot)) return 0;

        decayedCount += _store.DecayStaleMemories();

        var activeMemories = _store.Query(new MemoryFilter
        {
            Freshness = FreshnessFilter.FreshAndAging,
            Limit = 10000
        });

        foreach (var memory in activeMemories)
        {
            if (ShouldDecayDueToPathChanges(memory, topology, projectRoot))
            {
                _store.UpdateFreshness(memory.Id, FreshnessStatus.Stale);
                decayedCount++;
                _logger.LogInformation("记忆 [{Id}] 因关联文件发生变更，已降级为 Stale", memory.Id);
            }
        }

        return decayedCount;
    }

    private bool ShouldDecayDueToPathChanges(MemoryEntry memory, TopologySnapshot topology, string projectRoot)
    {
        var referenceTime = memory.LastVerifiedAt ?? memory.CreatedAt;

        if (!string.IsNullOrEmpty(memory.NodeId))
        {
            var node = topology.Nodes.FirstOrDefault(n => n.Id == memory.NodeId);
            if (node?.RelativePath != null)
            {
                var modulePath = Path.Combine(projectRoot, node.RelativePath);
                if (HasRecentChanges(modulePath, referenceTime))
                {
                    return true;
                }
            }
        }

        if (memory.PathPatterns.Count > 0)
        {
            foreach (var pattern in memory.PathPatterns)
            {
                var fullPath = Path.Combine(projectRoot, pattern);
                if (HasRecentChanges(fullPath, referenceTime))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool HasRecentChanges(string path, DateTime referenceTime)
    {
        if (!Directory.Exists(path) && !File.Exists(path))
            return false;

        try
        {
            if (File.Exists(path))
            {
                return File.GetLastWriteTimeUtc(path) > referenceTime;
            }

            if (Directory.Exists(path))
            {
                var dirInfo = new DirectoryInfo(path);
                var files = dirInfo.EnumerateFiles("*.*", SearchOption.AllDirectories)
                                   .Where(f => !f.FullName.Contains("/.git/") && !f.FullName.Contains("\\.git\\") &&
                                               !f.FullName.Contains("/.dna/") && !f.FullName.Contains("\\.dna\\"));
                
                foreach (var file in files)
                {
                    if (file.LastWriteTimeUtc > referenceTime)
                    {
                        return true;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "检查路径变更失败: {Path}", path);
        }

        return false;
    }
}
