using System.Text.Json;
using Dna.Knowledge.FileProtocol.Models;
using Dna.Knowledge.TopoGraph.Models.Nodes;
using Dna.Knowledge.TopoGraph.Models.Registrations;
using Dna.Knowledge.TopoGraph.Models.Snapshots;
using Dna.Knowledge.TopoGraph.Models.ValueObjects;

namespace Dna.Knowledge.FileProtocol;

/// <summary>
/// Knowledge 层文件协议的读写能力。
/// 负责 .agentic-os/knowledge/modules/ 下 module.json + identity.md + dependencies.json 的加载与保存。
/// </summary>
public sealed class KnowledgeFileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // ============== 读取 ==============

    /// <summary>扫描 knowledge/modules/ 下所有模块，返回模块文件列表</summary>
    public List<ModuleFile> LoadModules(string agenticOsPath)
    {
        var modulesRoot = FileProtocolPaths.GetModulesRoot(agenticOsPath);
        if (!Directory.Exists(modulesRoot))
            return [];

        var results = new List<ModuleFile>();
        ScanModulesRecursive(modulesRoot, agenticOsPath, results);
        return results;
    }

    /// <summary>读取单个模块的 module.json</summary>
    public ModuleFile? LoadModule(string agenticOsPath, string uid)
    {
        var filePath = FileProtocolPaths.GetModuleFilePath(agenticOsPath, uid);
        if (!File.Exists(filePath))
            return null;

        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<ModuleFile>(json, ReadOptions);
    }

    /// <summary>读取模块的 identity.md 内容</summary>
    public string? LoadIdentity(string agenticOsPath, string uid)
    {
        var filePath = FileProtocolPaths.GetIdentityFilePath(agenticOsPath, uid);
        return File.Exists(filePath) ? File.ReadAllText(filePath) : null;
    }

    /// <summary>读取模块的 dependencies.json</summary>
    public List<DependencyEntry> LoadDependencies(string agenticOsPath, string uid)
    {
        var filePath = FileProtocolPaths.GetDependenciesFilePath(agenticOsPath, uid);
        if (!File.Exists(filePath))
            return [];

        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<List<DependencyEntry>>(json, ReadOptions) ?? [];
    }

    /// <summary>
    /// 从 .agentic-os/knowledge/ 加载所有模块并转换为 TopologyModelDefinition，
    /// 可直接传递给 TopologyModelBuilder.Build() 构建对象图。
    /// </summary>
    public TopologyModelDefinition LoadAsDefinition(string agenticOsPath)
    {
        var modules = LoadModules(agenticOsPath);
        ProjectNodeRegistration? project = null;
        var departments = new List<DepartmentNodeRegistration>();
        var technicals = new List<TechnicalNodeRegistration>();
        var teams = new List<TeamNodeRegistration>();

        foreach (var m in modules)
        {
            var identity = LoadIdentity(agenticOsPath, m.Uid);
            var deps = LoadDependencies(agenticOsPath, m.Uid);

            switch (m.Type)
            {
                case TopologyNodeKind.Project:
                    project = ToProjectRegistration(m, identity);
                    break;
                case TopologyNodeKind.Department:
                    departments.Add(ToDepartmentRegistration(m, identity));
                    break;
                case TopologyNodeKind.Technical:
                    technicals.Add(ToTechnicalRegistration(m, identity, deps));
                    break;
                case TopologyNodeKind.Team:
                    teams.Add(ToTeamRegistration(m, identity, deps));
                    break;
            }
        }

        return new TopologyModelDefinition
        {
            Project = project,
            Departments = departments,
            TechnicalNodes = technicals,
            TeamNodes = teams
        };
    }

    // ============== 写入 ==============

    /// <summary>保存单个模块的三个文件</summary>
    public void SaveModule(string agenticOsPath, ModuleFile module,
        string? identityMarkdown = null, List<DependencyEntry>? dependencies = null)
    {
        var moduleDir = FileProtocolPaths.GetModuleDir(agenticOsPath, module.Uid);
        Directory.CreateDirectory(moduleDir);

        // module.json
        SortArrays(module);
        var json = JsonSerializer.Serialize(module, JsonOptions);
        File.WriteAllText(Path.Combine(moduleDir, FileProtocolPaths.ModuleFileName), json + "\n");

        // identity.md
        if (identityMarkdown != null)
            File.WriteAllText(Path.Combine(moduleDir, FileProtocolPaths.IdentityFileName), identityMarkdown);

        // dependencies.json
        if (dependencies is { Count: > 0 })
        {
            var sorted = dependencies.OrderBy(d => d.Target, StringComparer.Ordinal).ToList();
            var depsJson = JsonSerializer.Serialize(sorted, JsonOptions);
            File.WriteAllText(Path.Combine(moduleDir, FileProtocolPaths.DependenciesFileName), depsJson + "\n");
        }
    }

    /// <summary>从 TopologyModelSnapshot 导出所有模块到文件协议</summary>
    public void SaveFromSnapshot(string agenticOsPath, TopologyModelSnapshot snapshot)
    {
        foreach (var node in snapshot.Nodes)
        {
            var module = ToModuleFile(node);
            var deps = GetDependenciesFromSnapshot(snapshot, node.Id);
            var identity = node.Knowledge.Identity;
            SaveModule(agenticOsPath, module, identity, deps);
        }
    }

    // ============== 私有方法 ==============

    private void ScanModulesRecursive(string dir, string agenticOsPath, List<ModuleFile> results)
    {
        var moduleJsonPath = Path.Combine(dir, FileProtocolPaths.ModuleFileName);
        if (File.Exists(moduleJsonPath))
        {
            var json = File.ReadAllText(moduleJsonPath);
            var module = JsonSerializer.Deserialize<ModuleFile>(json, ReadOptions);
            if (module != null)
                results.Add(module);
        }

        foreach (var subDir in Directory.GetDirectories(dir))
            ScanModulesRecursive(subDir, agenticOsPath, results);
    }

    private static void SortArrays(ModuleFile m)
    {
        m.Keywords?.Sort(StringComparer.Ordinal);
        m.ManagedPaths?.Sort(StringComparer.Ordinal);
        m.CapabilityTags?.Sort(StringComparer.Ordinal);
        m.Deliverables?.Sort(StringComparer.Ordinal);
    }

    // --- 文件模型 → 注册模型转换 ---

    private static ProjectNodeRegistration ToProjectRegistration(ModuleFile m, string? identity) => new()
    {
        Id = m.Uid,
        Name = m.Name,
        Summary = ExtractSummary(identity),
        Vision = m.Vision,
        Steward = m.Steward,
        Knowledge = new TopologyKnowledgeSummary { Identity = identity }
    };

    private static DepartmentNodeRegistration ToDepartmentRegistration(ModuleFile m, string? identity) => new()
    {
        Id = m.Uid,
        Name = m.Name,
        ParentId = m.Parent,
        Summary = ExtractSummary(identity),
        DisciplineCode = m.DisciplineCode ?? string.Empty,
        Scope = m.Scope,
        Knowledge = new TopologyKnowledgeSummary { Identity = identity }
    };

    private static TechnicalNodeRegistration ToTechnicalRegistration(
        ModuleFile m, string? identity, List<DependencyEntry> deps) => new()
    {
        Id = m.Uid,
        Name = m.Name,
        ParentId = m.Parent,
        Summary = ExtractSummary(identity),
        Maintainer = m.Maintainer,
        PathBinding = new ModulePathBinding { ManagedPaths = m.ManagedPaths ?? [] },
        DeclaredDependencies = deps.Select(d => d.Target).ToList(),
        CapabilityTags = m.CapabilityTags ?? [],
        Contract = ExtractContract(identity),
        Knowledge = new TopologyKnowledgeSummary { Identity = identity }
    };

    private static TeamNodeRegistration ToTeamRegistration(
        ModuleFile m, string? identity, List<DependencyEntry> deps) => new()
    {
        Id = m.Uid,
        Name = m.Name,
        ParentId = m.Parent,
        Summary = ExtractSummary(identity),
        Maintainer = m.Maintainer,
        PathBinding = new ModulePathBinding { ManagedPaths = m.ManagedPaths ?? [] },
        TechnicalDependencies = deps.Select(d => d.Target).ToList(),
        BusinessObjective = m.BusinessObjective,
        Deliverables = m.Deliverables ?? [],
        Knowledge = new TopologyKnowledgeSummary { Identity = identity }
    };

    // --- 拓扑节点 → 文件模型转换 ---

    private static ModuleFile ToModuleFile(TopologyNode node)
    {
        var m = new ModuleFile
        {
            Uid = node.Id,
            Name = node.Name,
            Type = node.Kind,
            Parent = node.ParentId
        };

        switch (node)
        {
            case ProjectNode p:
                m.Vision = p.Vision;
                m.Steward = p.Steward;
                break;
            case DepartmentNode d:
                m.DisciplineCode = d.DisciplineCode;
                m.Scope = d.Scope;
                break;
            case TechnicalNode t:
                m.Maintainer = t.Maintainer;
                m.ManagedPaths = t.PathBinding.ManagedPaths.Count > 0 ? t.PathBinding.ManagedPaths : null;
                m.CapabilityTags = t.CapabilityTags.Count > 0 ? t.CapabilityTags : null;
                m.Keywords = null;
                break;
            case TeamNode tm:
                m.Maintainer = tm.Maintainer;
                m.ManagedPaths = tm.PathBinding.ManagedPaths.Count > 0 ? tm.PathBinding.ManagedPaths : null;
                m.BusinessObjective = tm.BusinessObjective;
                m.Deliverables = tm.Deliverables.Count > 0 ? tm.Deliverables : null;
                break;
        }

        return m;
    }

    private static List<DependencyEntry> GetDependenciesFromSnapshot(
        TopologyModelSnapshot snapshot, string nodeId)
    {
        return snapshot.Dependencies
            .Where(r => r.FromId == nodeId)
            .Select(r => new DependencyEntry
            {
                Target = r.ToId,
                Type = "Association"
            })
            .OrderBy(d => d.Target, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>从 identity.md 中提取 ## Summary 段落内容</summary>
    private static string? ExtractSummary(string? identity)
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

    /// <summary>从 identity.md 中提取 ## Contract 段落内容</summary>
    private static ModuleContract ExtractContract(string? identity)
    {
        if (string.IsNullOrWhiteSpace(identity))
            return new ModuleContract();

        var lines = identity.Split('\n');
        var inContract = false;
        var contractLines = new List<string>();

        foreach (var line in lines)
        {
            if (line.StartsWith("## Contract", StringComparison.OrdinalIgnoreCase))
            {
                inContract = true;
                continue;
            }

            if (inContract && line.StartsWith("## ", StringComparison.Ordinal))
                break;

            if (inContract)
                contractLines.Add(line);
        }

        var apiLines = contractLines
            .Where(l => l.TrimStart().StartsWith("- ", StringComparison.Ordinal))
            .Select(l => l.TrimStart()[2..].Trim())
            .Where(l => l.Length > 0)
            .ToList();

        return new ModuleContract { PublicApi = apiLines };
    }
}
