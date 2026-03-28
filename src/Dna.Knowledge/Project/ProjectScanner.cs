using Dna.Knowledge.Models;
using Dna.Knowledge.Project.Models;

namespace Dna.Knowledge.Project;

/// <summary>
/// 工程目录扫描器 — 按需单层扫描。
/// 从指定目录扫描直接子目录，标注模块注册状态。
/// </summary>
public static class ProjectScanner
{
    /// <summary>
    /// 扫描指定目录的直接子目录。
    /// </summary>
    public static List<ProjectFileNode> ScanDirectory(
        string projectRoot,
        string relativePath,
        ArchitectureManifest architecture,
        ModulesManifest manifest)
    {
        var excludes = DefaultExcludes.BuildWithCustom(architecture.ExcludeDirs);

        var context = BuildContext(manifest);

        var fullPath = string.IsNullOrEmpty(relativePath)
            ? projectRoot
            : Path.Combine(projectRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

        if (!Directory.Exists(fullPath)) return [];

        return ScanChildren(fullPath, NormalizePath(relativePath), context, excludes);
    }

    // ═══════════════════════════════════════════

    private static List<ProjectFileNode> ScanChildren(
        string parentFullPath,
        string parentRelPath,
        ScanContext context,
        HashSet<string> excludes)
    {
        var children = new List<ProjectFileNode>();
        try
        {
            foreach (var subdir in Directory.GetDirectories(parentFullPath).OrderBy(Path.GetFileName))
            {
                var subName = Path.GetFileName(subdir);
                if (excludes.Contains(subName)) continue;

                var subRelPath = string.IsNullOrEmpty(parentRelPath)
                    ? subName
                    : $"{parentRelPath}/{subName}";

                var child = BuildNode(subdir, subRelPath, context, excludes);
                if (child == null) continue;

                child.HasChildren = HasVisibleSubDirectories(subdir, excludes);
                children.Add(child);
            }
        }
        catch { /* permission denied */ }

        return children;
    }

    private static ProjectFileNode? BuildNode(
        string fullPath,
        string relativePath,
        ScanContext context,
        HashSet<string> excludes)
    {
        var dirName = Path.GetFileName(fullPath);
        if (string.IsNullOrEmpty(dirName))
            dirName = Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (excludes.Contains(dirName)) return null;

        var normalizedRelPath = NormalizePath(relativePath);

        var node = new ProjectFileNode
        {
            Name = dirName,
            Path = normalizedRelPath
        };

        if (context.ModulesByPath.TryGetValue(normalizedRelPath, out var reg))
        {
            var isCW = reg.module.IsCrossWorkModule;
            var isRoot = string.Equals(reg.discipline, "root", StringComparison.OrdinalIgnoreCase);
            node.Status = isCW ? FileNodeStatus.CrossWork : FileNodeStatus.Registered;
            node.StatusLabel = isCW ? "工作组模块" : "普通模块";
            node.Badge = isCW
                ? isRoot
                    ? $"项目工作组({reg.module.Participants.Count}成员)"
                    : $"{reg.discipline} · 工作组({reg.module.Participants.Count}成员)"
                : $"{reg.discipline}/L{reg.module.Layer}";
            node.Module = new FileNodeModuleInfo
            {
                Id = reg.module.Id,
                Name = reg.module.Name,
                Discipline = reg.discipline,
                Layer = reg.module.Layer,
                IsCrossWorkModule = isCW
            };
            node.Actions = new FileNodeActions { CanEdit = true };
        }
        else
        {
            if (context.ContainerPaths.Contains(normalizedRelPath))
            {
                node.Status = FileNodeStatus.Container;
                node.StatusLabel = "模块容器";
                node.Badge = "容器";
            }
            else
            {
                node.Status = FileNodeStatus.Candidate;
                node.StatusLabel = "候选目录";
            }
            node.Actions = new FileNodeActions
            {
                CanRegister = true,
                SuggestedDiscipline = GuessDiscipline(normalizedRelPath),
                SuggestedLayer = GuessSiblingLayer(normalizedRelPath, context.ModulesByPath)
            };
        }

        return node;
    }

    private static bool HasVisibleSubDirectories(string fullPath, HashSet<string> excludes)
    {
        try
        {
            foreach (var subdir in Directory.GetDirectories(fullPath))
            {
                var name = Path.GetFileName(subdir);
                if (!excludes.Contains(name)) return true;
            }
        }
        catch { /* permission denied */ }
        return false;
    }

    // ═══════════════════════════════════════════

    private record ScanContext(
        Dictionary<string, (string discipline, ModuleRegistration module)> ModulesByPath,
        HashSet<string> ContainerPaths);

    private static ScanContext BuildContext(ModulesManifest manifest)
    {
        var modulesByPath = new Dictionary<string, (string discipline, ModuleRegistration module)>(StringComparer.OrdinalIgnoreCase);
        foreach (var (discipline, modules) in manifest.Disciplines)
            foreach (var m in modules)
                modulesByPath[NormalizePath(m.Path)] = (discipline, m);

        var containerPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in modulesByPath.Keys)
        {
            if (string.IsNullOrEmpty(path)) continue;
            var current = path;
            while (current.Contains('/'))
            {
                current = current[..current.LastIndexOf('/')];
                if (!string.IsNullOrEmpty(current))
                    containerPaths.Add(current);
            }
        }

        return new ScanContext(modulesByPath, containerPaths);
    }

    // ═══════════════════════════════════════════

    private static string? GuessDiscipline(string path)
    {
        var lower = path.ToLowerInvariant();
        if (lower.StartsWith("src/") || lower.StartsWith("code/")) return "engineering";
        if (lower.StartsWith("art/")) return "art";
        if (lower.StartsWith("design/")) return "design";
        if (lower.StartsWith("devops/") || lower.StartsWith("ci/")) return "devops";
        if (lower.StartsWith("qa/") || lower.StartsWith("test/")) return "qa";
        if (lower.StartsWith("tools/")) return "tech-support";
        return null;
    }

    private static int? GuessSiblingLayer(
        string path,
        Dictionary<string, (string discipline, ModuleRegistration module)> modulesByPath)
    {
        var parentPath = path.Contains('/')
            ? path[..path.LastIndexOf('/')]
            : "";

        foreach (var (regPath, reg) in modulesByPath)
        {
            if (regPath.Contains('/'))
            {
                var regParent = regPath[..regPath.LastIndexOf('/')];
                if (string.Equals(regParent, parentPath, StringComparison.OrdinalIgnoreCase))
                    return reg.module.Layer;
            }
        }
        return null;
    }

    private static string NormalizePath(string path) =>
        path.Replace('\\', '/').Trim('/');
}
