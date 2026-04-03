using Dna.Knowledge.FileProtocol.Models;
using Dna.Knowledge.TopoGraph.Models.Nodes;
using Dna.Knowledge.Workspace.Models;

namespace Dna.Knowledge.FileProtocol;

/// <summary>
/// 将文件协议模型转换为旧版 manifest 格式，
/// 使 GraphEngine（旧系统）能从 .agentic-os/ 文件加载数据。
/// </summary>
public static class FileProtocolLegacyAdapter
{
    /// <summary>
    /// 从 .agentic-os/ 加载模块并转换为旧版 ArchitectureManifest + ModulesManifest。
    /// </summary>
    public static (ArchitectureManifest architecture, ModulesManifest modules) LoadAsLegacyManifests(
        string agenticOsPath)
    {
        var store = new KnowledgeFileStore();
        var allModules = store.LoadModules(agenticOsPath);

        if (allModules.Count == 0)
            return (new ArchitectureManifest(), new ModulesManifest());

        var architecture = BuildArchitecture(allModules);
        var modulesManifest = BuildModulesManifest(allModules, agenticOsPath, store);

        return (architecture, modulesManifest);
    }

    /// <summary>从 Department 节点推导 ArchitectureManifest</summary>
    private static ArchitectureManifest BuildArchitecture(List<ModuleFile> allModules)
    {
        var disciplines = new Dictionary<string, DisciplineDefinition>(StringComparer.OrdinalIgnoreCase);

        foreach (var m in allModules.Where(m => m.Type == TopologyNodeKind.Department))
        {
            var code = m.DisciplineCode ?? m.Uid.Split('/').Last().ToLowerInvariant();
            if (disciplines.ContainsKey(code))
                continue;

            disciplines[code] = new DisciplineDefinition
            {
                DisplayName = m.Name,
                RoleId = code,
                Layers =
                [
                    new LayerDefinition { Level = 1, Name = "foundation" },
                    new LayerDefinition { Level = 2, Name = "system" },
                    new LayerDefinition { Level = 3, Name = "application" }
                ]
            };
        }

        // 至少有一个默认 discipline
        if (disciplines.Count == 0)
        {
            disciplines["default"] = new DisciplineDefinition
            {
                DisplayName = "Default",
                RoleId = "default",
                Layers =
                [
                    new LayerDefinition { Level = 1, Name = "foundation" },
                    new LayerDefinition { Level = 2, Name = "system" },
                    new LayerDefinition { Level = 3, Name = "application" }
                ]
            };
        }

        return new ArchitectureManifest { Disciplines = disciplines };
    }

    /// <summary>构建 ModulesManifest，按 discipline 分组</summary>
    private static ModulesManifest BuildModulesManifest(
        List<ModuleFile> allModules, string agenticOsPath, KnowledgeFileStore store)
    {
        var disciplineModules = new Dictionary<string, List<ModuleRegistration>>(StringComparer.OrdinalIgnoreCase);

        foreach (var m in allModules)
        {
            var registration = ToModuleRegistration(m, agenticOsPath, store);
            var discipline = InferDiscipline(m, allModules);

            if (!disciplineModules.TryGetValue(discipline, out var list))
            {
                list = [];
                disciplineModules[discipline] = list;
            }

            list.Add(registration);
        }

        return new ModulesManifest { Disciplines = disciplineModules };
    }

    /// <summary>将 ModuleFile 转换为旧版 ModuleRegistration</summary>
    private static ModuleRegistration ToModuleRegistration(
        ModuleFile m, string agenticOsPath, KnowledgeFileStore store)
    {
        var deps = store.LoadDependencies(agenticOsPath, m.Uid);
        var identity = store.LoadIdentity(agenticOsPath, m.Uid);

        return new ModuleRegistration
        {
            Id = m.Uid,
            Name = m.Name,
            Path = m.Uid.Replace('/', Path.DirectorySeparatorChar),
            Layer = InferLayer(m.Type),
            ParentModuleId = m.Parent,
            ManagedPaths = m.ManagedPaths,
            Dependencies = deps.Select(d => d.Target).ToList(),
            Maintainer = m.Maintainer,
            Summary = ExtractSummaryFromIdentity(identity),
            Boundary = InferBoundary(m.Type),
            Metadata = new Dictionary<string, string>
            {
                ["fileProtocolType"] = m.Type.ToString()
            }
        };
    }

    /// <summary>从模块类型推导旧版 Layer 值</summary>
    private static int InferLayer(TopologyNodeKind type) => type switch
    {
        TopologyNodeKind.Project => 0,
        TopologyNodeKind.Department => 0,
        TopologyNodeKind.Technical => 1,
        TopologyNodeKind.Team => 3,
        _ => 2
    };

    /// <summary>推导模块所属的 discipline</summary>
    private static string InferDiscipline(ModuleFile m, List<ModuleFile> allModules)
    {
        // 向上找最近的 Department 祖先的 disciplineCode
        var current = m;
        while (current != null)
        {
            if (current.Type == TopologyNodeKind.Department && !string.IsNullOrEmpty(current.DisciplineCode))
                return current.DisciplineCode;

            if (current.Parent == null)
                break;

            current = allModules.FirstOrDefault(x =>
                string.Equals(x.Uid, current.Parent, StringComparison.Ordinal));
        }

        return "default";
    }

    private static string? InferBoundary(TopologyNodeKind type) => type switch
    {
        TopologyNodeKind.Technical => "semi-open",
        TopologyNodeKind.Team => "open",
        _ => null
    };

    private static string? ExtractSummaryFromIdentity(string? identity)
    {
        if (string.IsNullOrWhiteSpace(identity))
            return null;

        var lines = identity.Split('\n');
        var inSummary = false;
        var summaryLines = new List<string>();

        foreach (var line in lines)
        {
            if (line.StartsWith("## Summary", StringComparison.OrdinalIgnoreCase))
            {
                inSummary = true;
                continue;
            }

            if (inSummary && line.StartsWith("## ", StringComparison.Ordinal))
                break;

            if (inSummary)
                summaryLines.Add(line);
        }

        var result = string.Join('\n', summaryLines).Trim();
        return result.Length > 0 ? result : null;
    }
}
