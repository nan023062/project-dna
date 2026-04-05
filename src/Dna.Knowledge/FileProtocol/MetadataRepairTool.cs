using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Dna.Knowledge.FileProtocol.Models;
using Dna.Knowledge.Workspace;
using Dna.Knowledge.Workspace.Models;

namespace Dna.Knowledge.FileProtocol;

/// <summary>
/// 目录元数据与知识引用的对账修复工具。
/// 扫描物理目录 .agentic.meta + 知识层 managedPaths 引用，识别不一致并修复。
/// </summary>
public sealed class MetadataRepairTool
{
    private static readonly Regex StableGuidPattern = new("^[0-9a-fA-F]{32}$", RegexOptions.Compiled);

    private static readonly JsonSerializerOptions MetaJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // ============== 数据模型 ==============

    /// <summary>单条修复问题</summary>
    public sealed class RepairIssue
    {
        public required string Type { get; init; }
        public required string Message { get; init; }
        public required RepairSeverity Severity { get; init; }
        public string? DirectoryPath { get; init; }
        public string? StableGuid { get; init; }
        public string? ModuleUid { get; init; }
        public RepairAction? SuggestedAction { get; init; }
    }

    public enum RepairSeverity { Info, Warning, Error }

    /// <summary>修复动作</summary>
    public sealed class RepairAction
    {
        public required string Type { get; init; }
        public required string Description { get; init; }
        public string? FilePath { get; init; }
        public string? OldValue { get; init; }
        public string? NewValue { get; init; }
    }

    /// <summary>修复报告</summary>
    public sealed class RepairReport
    {
        public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;
        public int DirectoriesScanned { get; init; }
        public int MetaFilesFound { get; init; }
        public int ModulesScanned { get; init; }
        public int ReferencesChecked { get; init; }
        public List<RepairIssue> Issues { get; init; } = [];
        public List<string> AppliedFixes { get; init; } = [];
        public List<string> BackedUpFiles { get; init; } = [];

        public bool HasErrors => Issues.Any(i => i.Severity == RepairSeverity.Error);
        public bool HasWarnings => Issues.Any(i => i.Severity == RepairSeverity.Warning);
        public int AutoFixableCount => Issues.Count(i => i.SuggestedAction != null &&
            i.SuggestedAction.Type != "manual-confirm");
    }

    // ============== Step 1: 扫描物理目录 ==============

    /// <summary>物理目录的 .agentic.meta 快照</summary>
    private sealed class MetaSnapshot
    {
        public string RelativePath { get; init; } = string.Empty;
        public string FullPath { get; init; } = string.Empty;
        public string? StableGuid { get; init; }
        public bool Exists { get; init; }
        public bool IsValid { get; init; }
    }

    private static List<MetaSnapshot> ScanPhysicalMeta(string projectRoot)
    {
        var results = new List<MetaSnapshot>();
        var excludes = DefaultExcludes.BuildWithCustom(customDirs: null);

        ScanRecursive(projectRoot, projectRoot, excludes, results);
        return results;
    }

    private static void ScanRecursive(string root, string current,
        HashSet<string> excludes, List<MetaSnapshot> results)
    {
        var dirName = Path.GetFileName(current);
        if (DefaultExcludes.IsExcludedDirectory(dirName, excludes))
            return;

        var relative = Path.GetRelativePath(root, current).Replace('\\', '/');
        if (relative == ".") relative = string.Empty;

        var metaPath = Path.Combine(current, WorkspaceConstants.Metadata.FileName);
        if (File.Exists(metaPath))
        {
            try
            {
                var json = File.ReadAllText(metaPath);
                var doc = JsonSerializer.Deserialize<WorkspaceDirectoryMetadataDocument>(json, MetaJsonOptions);
                results.Add(new MetaSnapshot
                {
                    RelativePath = relative,
                    FullPath = current,
                    StableGuid = doc?.StableGuid,
                    Exists = true,
                    IsValid = doc != null && StableGuidPattern.IsMatch(doc.StableGuid ?? "")
                });
            }
            catch
            {
                results.Add(new MetaSnapshot
                {
                    RelativePath = relative,
                    FullPath = current,
                    Exists = true,
                    IsValid = false
                });
            }
        }
        else
        {
            results.Add(new MetaSnapshot
            {
                RelativePath = relative,
                FullPath = current,
                Exists = false,
                IsValid = false
            });
        }

        try
        {
            foreach (var subDir in Directory.GetDirectories(current))
                ScanRecursive(root, subDir, excludes, results);
        }
        catch (UnauthorizedAccessException) { }
    }

    // ============== Step 2: 扫描知识引用 ==============

    /// <summary>模块引用的 managedPaths guid</summary>
    private sealed class ModuleReference
    {
        public string ModuleUid { get; init; } = string.Empty;
        public string ReferencedGuid { get; init; } = string.Empty;
    }

    private static (List<ModuleFile> modules, List<ModuleReference> references) ScanKnowledgeReferences(
        string agenticOsPath)
    {
        var store = new KnowledgeFileStore();
        var modules = store.LoadModules(agenticOsPath);
        var references = new List<ModuleReference>();

        foreach (var m in modules)
        {
            if (m.ManagedPaths == null) continue;
            foreach (var guid in m.ManagedPaths)
            {
                references.Add(new ModuleReference
                {
                    ModuleUid = m.Uid,
                    ReferencedGuid = guid
                });
            }
        }

        return (modules, references);
    }

    // ============== Step 3: 一致性判断 ==============

    /// <summary>
    /// dry-run：扫描并报告所有问题，不做任何修改。
    /// </summary>
    public RepairReport DryRun(string projectRoot, string agenticOsPath)
    {
        var metas = ScanPhysicalMeta(projectRoot);
        var (modules, references) = ScanKnowledgeReferences(agenticOsPath);

        var guidToPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var issues = new List<RepairIssue>();

        // 构建 guid → path 映射
        foreach (var meta in metas.Where(m => m.Exists && m.IsValid && m.StableGuid != null))
        {
            if (guidToPath.TryGetValue(meta.StableGuid!, out var existingPath))
            {
                // 重复 GUID
                issues.Add(new RepairIssue
                {
                    Type = "duplicate-guid",
                    Severity = RepairSeverity.Error,
                    Message = $"GUID {meta.StableGuid} 同时出现在 {existingPath} 和 {meta.RelativePath}",
                    StableGuid = meta.StableGuid,
                    DirectoryPath = meta.RelativePath,
                    SuggestedAction = new RepairAction
                    {
                        Type = "regenerate-guid",
                        Description = $"为 {meta.RelativePath} 生成新 GUID",
                        FilePath = Path.Combine(meta.FullPath, WorkspaceConstants.Metadata.FileName)
                    }
                });
            }
            else
            {
                guidToPath[meta.StableGuid!] = meta.RelativePath;
            }
        }

        // 检查：.agentic.meta 缺失
        foreach (var meta in metas.Where(m => !m.Exists))
        {
            issues.Add(new RepairIssue
            {
                Type = "missing-meta",
                Severity = RepairSeverity.Warning,
                Message = $"目录 {meta.RelativePath} 缺少 .agentic.meta",
                DirectoryPath = meta.RelativePath,
                SuggestedAction = new RepairAction
                {
                    Type = "create-meta",
                    Description = $"为 {meta.RelativePath} 创建 .agentic.meta",
                    FilePath = Path.Combine(meta.FullPath, WorkspaceConstants.Metadata.FileName)
                }
            });
        }

        // 检查：.agentic.meta 格式非法
        foreach (var meta in metas.Where(m => m.Exists && !m.IsValid))
        {
            issues.Add(new RepairIssue
            {
                Type = "invalid-meta",
                Severity = RepairSeverity.Error,
                Message = $"目录 {meta.RelativePath} 的 .agentic.meta 格式非法",
                DirectoryPath = meta.RelativePath,
                SuggestedAction = new RepairAction
                {
                    Type = "regenerate-meta",
                    Description = $"重建 {meta.RelativePath} 的 .agentic.meta",
                    FilePath = Path.Combine(meta.FullPath, WorkspaceConstants.Metadata.FileName)
                }
            });
        }

        // 检查：模块引用了不存在的 GUID
        foreach (var reference in references)
        {
            if (!StableGuidPattern.IsMatch(reference.ReferencedGuid))
            {
                issues.Add(new RepairIssue
                {
                    Type = "invalid-reference-format",
                    Severity = RepairSeverity.Error,
                    Message = $"模块 {reference.ModuleUid} 的 managedPaths 引用格式非法: {reference.ReferencedGuid}",
                    ModuleUid = reference.ModuleUid,
                    StableGuid = reference.ReferencedGuid
                });
                continue;
            }

            if (!guidToPath.ContainsKey(reference.ReferencedGuid))
            {
                issues.Add(new RepairIssue
                {
                    Type = "dangling-reference",
                    Severity = RepairSeverity.Error,
                    Message = $"模块 {reference.ModuleUid} 引用的 GUID {reference.ReferencedGuid} 未在任何目录中找到",
                    ModuleUid = reference.ModuleUid,
                    StableGuid = reference.ReferencedGuid,
                    SuggestedAction = new RepairAction
                    {
                        Type = "manual-confirm",
                        Description = "需要人工确认：该 GUID 对应的目录可能已被删除或重命名"
                    }
                });
            }
        }

        // 检查：有 GUID 但没有任何模块引用（孤儿 meta）
        var referencedGuids = new HashSet<string>(
            references.Select(r => r.ReferencedGuid), StringComparer.OrdinalIgnoreCase);
        foreach (var meta in metas.Where(m => m.IsValid && m.StableGuid != null))
        {
            if (!referencedGuids.Contains(meta.StableGuid!))
            {
                issues.Add(new RepairIssue
                {
                    Type = "orphan-meta",
                    Severity = RepairSeverity.Info,
                    Message = $"目录 {meta.RelativePath} 有 .agentic.meta 但无模块引用",
                    DirectoryPath = meta.RelativePath,
                    StableGuid = meta.StableGuid
                });
            }
        }

        return new RepairReport
        {
            DirectoriesScanned = metas.Count,
            MetaFilesFound = metas.Count(m => m.Exists),
            ModulesScanned = modules.Count,
            ReferencesChecked = references.Count,
            Issues = issues
        };
    }

    // ============== Step 4: 执行修复 ==============

    /// <summary>
    /// apply：执行自动修复。只修复有确定性修复方案的问题，人工确认类跳过。
    /// </summary>
    public RepairReport Apply(string projectRoot, string agenticOsPath)
    {
        var report = DryRun(projectRoot, agenticOsPath);
        var applied = new List<string>();
        var backedUp = new List<string>();

        foreach (var issue in report.Issues)
        {
            if (issue.SuggestedAction == null)
                continue;

            switch (issue.SuggestedAction.Type)
            {
                case "create-meta":
                    if (issue.DirectoryPath != null)
                    {
                        var metaPath = Path.Combine(
                            Path.Combine(projectRoot, issue.DirectoryPath.Replace('/', Path.DirectorySeparatorChar)),
                            WorkspaceConstants.Metadata.FileName);
                        var doc = new WorkspaceDirectoryMetadataDocument();
                        var json = JsonSerializer.Serialize(doc, MetaJsonOptions);
                        File.WriteAllText(metaPath, json);
                        applied.Add($"创建: {metaPath}");
                    }
                    break;

                case "regenerate-meta":
                case "regenerate-guid":
                    if (issue.SuggestedAction.FilePath != null)
                    {
                        // 备份
                        if (File.Exists(issue.SuggestedAction.FilePath))
                        {
                            var backupPath = issue.SuggestedAction.FilePath + $".{DateTime.UtcNow:yyyyMMddHHmmss}.bak";
                            File.Copy(issue.SuggestedAction.FilePath, backupPath);
                            backedUp.Add(backupPath);
                        }

                        var newDoc = new WorkspaceDirectoryMetadataDocument();
                        var newJson = JsonSerializer.Serialize(newDoc, MetaJsonOptions);
                        File.WriteAllText(issue.SuggestedAction.FilePath, newJson);
                        applied.Add($"重建: {issue.SuggestedAction.FilePath} (新 GUID: {newDoc.StableGuid})");
                    }
                    break;

                case "manual-confirm":
                    // 跳过，需要人工确认
                    break;
            }
        }

        report.AppliedFixes.AddRange(applied);
        report.BackedUpFiles.AddRange(backedUp);
        return report;
    }

    /// <summary>生成可读的文本报告</summary>
    public static string FormatReport(RepairReport report)
    {
        var lines = new List<string>
        {
            "# Metadata Repair Report",
            $"- 生成时间: {report.GeneratedAt:O}",
            $"- 扫描目录: {report.DirectoriesScanned}",
            $"- Meta 文件: {report.MetaFilesFound}",
            $"- 扫描模块: {report.ModulesScanned}",
            $"- 检查引用: {report.ReferencesChecked}",
            $"- 发现问题: {report.Issues.Count} (可自动修复: {report.AutoFixableCount})",
            ""
        };

        if (report.Issues.Count == 0)
        {
            lines.Add("没有发现任何问题。");
        }
        else
        {
            var grouped = report.Issues.GroupBy(i => i.Type);
            foreach (var group in grouped)
            {
                lines.Add($"## {group.Key} ({group.Count()})");
                foreach (var issue in group)
                {
                    var prefix = issue.Severity switch
                    {
                        RepairSeverity.Error => "[ERROR]",
                        RepairSeverity.Warning => "[WARN]",
                        _ => "[INFO]"
                    };
                    lines.Add($"  {prefix} {issue.Message}");
                    if (issue.SuggestedAction != null)
                        lines.Add($"         -> {issue.SuggestedAction.Description}");
                }
                lines.Add("");
            }
        }

        if (report.AppliedFixes.Count > 0)
        {
            lines.Add("## 已执行修复");
            foreach (var fix in report.AppliedFixes)
                lines.Add($"  - {fix}");
            lines.Add("");
        }

        if (report.BackedUpFiles.Count > 0)
        {
            lines.Add("## 已备份文件");
            foreach (var backup in report.BackedUpFiles)
                lines.Add($"  - {backup}");
        }

        return string.Join('\n', lines);
    }
}
