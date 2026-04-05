using System.Text.RegularExpressions;
using Dna.Knowledge.FileProtocol.Models;
using Dna.Knowledge.TopoGraph.Models.Nodes;

namespace Dna.Knowledge.FileProtocol;

/// <summary>
/// .agentic-os/ 文件协议验证器。
/// 校验目录结构完整性、字段合法性和引用完整性。
/// </summary>
public sealed class FileProtocolValidator
{
    private static readonly Regex StableGuidPattern = new("^[0-9a-fA-F]{32}$", RegexOptions.Compiled);

    /// <summary>校验结果</summary>
    public sealed class ValidationResult
    {
        public bool IsValid => Errors.Count == 0;
        public List<string> Errors { get; } = [];
        public List<string> Warnings { get; } = [];
    }

    /// <summary>
    /// 校验 .agentic-os/ 目录结构完整性和内容合法性。
    /// </summary>
    public ValidationResult Validate(string agenticOsPath)
    {
        var result = new ValidationResult();
        var store = new KnowledgeFileStore();

        // 1. 基础目录检查
        if (!Directory.Exists(agenticOsPath))
        {
            result.Errors.Add($"目录不存在: {agenticOsPath}");
            return result;
        }

        var modulesRoot = FileProtocolPaths.GetModulesRoot(agenticOsPath);
        if (!Directory.Exists(modulesRoot))
        {
            result.Warnings.Add("knowledge/modules/ 目录不存在，知识空间为空");
            return result;
        }

        // 2. 加载所有模块
        var modules = store.LoadModules(agenticOsPath);
        if (modules.Count == 0)
        {
            result.Warnings.Add("未找到任何模块定义");
            return result;
        }

        var moduleMap = new Dictionary<string, ModuleFile>(StringComparer.Ordinal);
        foreach (var m in modules)
        {
            if (string.IsNullOrWhiteSpace(m.Uid))
            {
                result.Errors.Add("存在 uid 为空的 module.json");
                continue;
            }

            if (moduleMap.ContainsKey(m.Uid))
            {
                result.Errors.Add($"UID 重复: {m.Uid}");
                continue;
            }

            moduleMap[m.Uid] = m;
        }

        // 3. 逐模块校验
        foreach (var m in modules)
        {
            if (string.IsNullOrWhiteSpace(m.Uid)) continue;

            ValidateModule(m, moduleMap, agenticOsPath, store, result);
        }

        // 4. 循环父子关系检查
        ValidateNoCyclicParent(moduleMap, result);

        // 5. 循环依赖检查
        ValidateNoCyclicDependency(moduleMap, agenticOsPath, store, result);

        return result;
    }

    private void ValidateModule(ModuleFile m, Dictionary<string, ModuleFile> moduleMap,
        string agenticOsPath, KnowledgeFileStore store, ValidationResult result)
    {
        // 必需字段
        if (string.IsNullOrWhiteSpace(m.Name))
            result.Errors.Add($"[{m.Uid}] name 为空");

        // type 合法性
        if (!Enum.IsDefined(typeof(TopologyNodeKind), m.Type))
            result.Errors.Add($"[{m.Uid}] type 不合法: {m.Type}");

        // UID 与目录路径一致性
        var expectedDir = FileProtocolPaths.GetModuleDir(agenticOsPath, m.Uid);
        if (!Directory.Exists(expectedDir))
            result.Errors.Add($"[{m.Uid}] 目录路径与 UID 不一致，目录不存在: {expectedDir}");

        // parent 引用完整性
        if (m.Parent != null && !moduleMap.ContainsKey(m.Parent))
            result.Errors.Add($"[{m.Uid}] parent 引用不存在: {m.Parent}");

        // Project 不应有 parent
        if (m.Type == TopologyNodeKind.Project && m.Parent != null)
            result.Errors.Add($"[{m.Uid}] Project 类型不应有 parent");

        // 非 Project 必须有 parent
        if (m.Type != TopologyNodeKind.Project && m.Parent == null)
            result.Warnings.Add($"[{m.Uid}] 非 Project 类型建议声明 parent");

        // 父子类型约束
        if (m.Parent != null && moduleMap.TryGetValue(m.Parent, out var parent))
        {
            ValidateParentChildType(parent.Type, m.Type, m.Uid, result);
        }

        // managedPaths StableGuid 格式
        if (m.ManagedPaths != null)
        {
            foreach (var guid in m.ManagedPaths)
            {
                if (!StableGuidPattern.IsMatch(guid))
                    result.Warnings.Add($"[{m.Uid}] managedPaths 中的值不是合法的 StableGuid: {guid}");
            }
        }

        // 依赖校验
        var deps = store.LoadDependencies(agenticOsPath, m.Uid);
        foreach (var dep in deps)
        {
            if (string.IsNullOrWhiteSpace(dep.Target))
            {
                result.Errors.Add($"[{m.Uid}] dependencies 中存在空 target");
                continue;
            }

            if (!moduleMap.ContainsKey(dep.Target))
                result.Errors.Add($"[{m.Uid}] 依赖目标不存在: {dep.Target}");

            // Team 不应被依赖
            if (moduleMap.TryGetValue(dep.Target, out var targetModule) &&
                targetModule.Type == TopologyNodeKind.Team)
            {
                result.Warnings.Add($"[{m.Uid}] 依赖了 Team 类型模块 {dep.Target}，Team 不应被其他模块依赖");
            }
        }
    }

    private static void ValidateParentChildType(TopologyNodeKind parentType,
        TopologyNodeKind childType, string childUid, ValidationResult result)
    {
        var valid = parentType switch
        {
            TopologyNodeKind.Project => childType == TopologyNodeKind.Department,
            TopologyNodeKind.Department => childType is TopologyNodeKind.Technical or TopologyNodeKind.Team,
            _ => false
        };

        if (!valid)
            result.Errors.Add(
                $"[{childUid}] 父模块类型 {parentType} 不允许包含 {childType} 类型的子模块");
    }

    private static void ValidateNoCyclicParent(Dictionary<string, ModuleFile> moduleMap,
        ValidationResult result)
    {
        foreach (var m in moduleMap.Values)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var current = m.Uid;

            while (current != null)
            {
                if (!visited.Add(current))
                {
                    result.Errors.Add($"检测到循环父子关系，涉及模块: {current}");
                    break;
                }

                if (moduleMap.TryGetValue(current, out var node))
                    current = node.Parent;
                else
                    break;
            }
        }
    }

    private static void ValidateNoCyclicDependency(Dictionary<string, ModuleFile> moduleMap,
        string agenticOsPath, KnowledgeFileStore store, ValidationResult result)
    {
        // 构建依赖邻接表
        var graph = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var m in moduleMap.Values)
        {
            var deps = store.LoadDependencies(agenticOsPath, m.Uid);
            graph[m.Uid] = deps.Select(d => d.Target)
                .Where(t => moduleMap.ContainsKey(t))
                .ToList();
        }

        // DFS 检测环
        var white = new HashSet<string>(moduleMap.Keys, StringComparer.Ordinal);
        var gray = new HashSet<string>(StringComparer.Ordinal);

        foreach (var uid in moduleMap.Keys)
        {
            if (white.Contains(uid) && HasCycleDfs(uid, graph, white, gray))
            {
                result.Errors.Add($"检测到循环依赖，涉及模块: {uid}");
                break;
            }
        }
    }

    private static bool HasCycleDfs(string node, Dictionary<string, List<string>> graph,
        HashSet<string> white, HashSet<string> gray)
    {
        white.Remove(node);
        gray.Add(node);

        if (graph.TryGetValue(node, out var neighbors))
        {
            foreach (var neighbor in neighbors)
            {
                if (gray.Contains(neighbor))
                    return true;
                if (white.Contains(neighbor) && HasCycleDfs(neighbor, graph, white, gray))
                    return true;
            }
        }

        gray.Remove(node);
        return false;
    }
}
