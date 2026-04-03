using Dna.Knowledge.TopoGraph.Contracts;
using Dna.Knowledge.TopoGraph.Models.Registrations;

namespace Dna.Knowledge.FileProtocol;

/// <summary>
/// 基于 .agentic-os/ 明文文件的 ITopoGraphDefinitionStore 实现。
/// 从 knowledge/modules/ 加载模块定义，构建 TopologyModelDefinition。
/// </summary>
public sealed class FileBasedDefinitionStore : ITopoGraphDefinitionStore
{
    private readonly KnowledgeFileStore _fileStore = new();
    private string _agenticOsPath = string.Empty;
    private bool _initialized;

    public void Initialize(string storePath)
    {
        // storePath 指向 .agentic-os/ 目录（或其父目录）
        // 需要找到包含 knowledge/modules/ 的 .agentic-os/ 路径
        _agenticOsPath = ResolveAgenticOsPath(storePath);
        _initialized = true;
    }

    public void Reload()
    {
        // 文件系统无需显式 reload，每次 LoadDefinition 都直接读文件
    }

    public TopologyModelDefinition LoadDefinition()
    {
        if (!_initialized || string.IsNullOrEmpty(_agenticOsPath))
            return new TopologyModelDefinition();

        var modulesRoot = FileProtocolPaths.GetModulesRoot(_agenticOsPath);
        if (!Directory.Exists(modulesRoot))
            return new TopologyModelDefinition();

        return _fileStore.LoadAsDefinition(_agenticOsPath);
    }

    public void SaveDefinition(TopologyModelDefinition definition)
    {
        if (!_initialized || string.IsNullOrEmpty(_agenticOsPath))
            return;

        // 从 definition 重建文件
        // 注意：这会覆盖现有文件，用于从运行时写回文件
        SaveDefinitionToFiles(definition);
    }

    /// <summary>检查文件协议知识空间是否存在</summary>
    public bool HasKnowledgeFiles()
    {
        if (!_initialized || string.IsNullOrEmpty(_agenticOsPath))
            return false;

        var modulesRoot = FileProtocolPaths.GetModulesRoot(_agenticOsPath);
        return Directory.Exists(modulesRoot) &&
               Directory.GetFiles(modulesRoot, FileProtocolPaths.ModuleFileName, SearchOption.AllDirectories).Length > 0;
    }

    /// <summary>获取解析后的 .agentic-os/ 路径</summary>
    public string AgenticOsPath => _agenticOsPath;

    private void SaveDefinitionToFiles(TopologyModelDefinition definition)
    {
        if (definition.Project != null)
        {
            SaveRegistrationAsModule(definition.Project, null);
        }

        foreach (var dept in definition.Departments)
            SaveRegistrationAsModule(dept, null);

        foreach (var tech in definition.TechnicalNodes)
            SaveRegistrationAsModule(tech, tech.DeclaredDependencies);

        foreach (var team in definition.TeamNodes)
            SaveRegistrationAsModule(team, team.TechnicalDependencies);
    }

    private void SaveRegistrationAsModule(TopologyNodeRegistration reg, List<string>? depTargets)
    {
        var module = RegistrationToModuleFile(reg);
        var identity = reg.Knowledge.Identity;
        var deps = depTargets?
            .Select(t => new Models.DependencyEntry { Target = t, Type = "Association" })
            .ToList();

        _fileStore.SaveModule(_agenticOsPath, module, identity, deps);
    }

    private static Models.ModuleFile RegistrationToModuleFile(TopologyNodeRegistration reg)
    {
        var m = new Models.ModuleFile
        {
            Uid = reg.Id,
            Name = reg.Name,
            Parent = reg.ParentId
        };

        switch (reg)
        {
            case ProjectNodeRegistration p:
                m.Type = TopoGraph.Models.Nodes.TopologyNodeKind.Project;
                m.Vision = p.Vision;
                m.Steward = p.Steward;
                break;
            case DepartmentNodeRegistration d:
                m.Type = TopoGraph.Models.Nodes.TopologyNodeKind.Department;
                m.DisciplineCode = d.DisciplineCode;
                m.Scope = d.Scope;
                break;
            case TechnicalNodeRegistration t:
                m.Type = TopoGraph.Models.Nodes.TopologyNodeKind.Technical;
                m.Maintainer = t.Maintainer;
                m.ManagedPaths = t.PathBinding.ManagedPaths.Count > 0 ? t.PathBinding.ManagedPaths : null;
                m.CapabilityTags = t.CapabilityTags.Count > 0 ? t.CapabilityTags : null;
                break;
            case TeamNodeRegistration tm:
                m.Type = TopoGraph.Models.Nodes.TopologyNodeKind.Team;
                m.Maintainer = tm.Maintainer;
                m.ManagedPaths = tm.PathBinding.ManagedPaths.Count > 0 ? tm.PathBinding.ManagedPaths : null;
                m.BusinessObjective = tm.BusinessObjective;
                m.Deliverables = tm.Deliverables.Count > 0 ? tm.Deliverables : null;
                break;
        }

        return m;
    }

    /// <summary>
    /// 从 storePath 解析 .agentic-os/ 路径。
    /// storePath 可能是 .agentic-os/ 本身，也可能是其下的子目录（如 knowledge/）。
    /// </summary>
    private static string ResolveAgenticOsPath(string storePath)
    {
        if (string.IsNullOrWhiteSpace(storePath))
            return string.Empty;

        var normalized = storePath.Replace('\\', '/').TrimEnd('/');

        // 如果路径本身就是 .agentic-os 目录
        if (normalized.EndsWith("/" + FileProtocolPaths.AgenticOsDir) ||
            Path.GetFileName(normalized) == FileProtocolPaths.AgenticOsDir)
            return storePath;

        // 如果路径是 .agentic-os 下的子目录（如 knowledge/ 或 memory/）
        var agenticOsDir = Path.GetDirectoryName(storePath);
        if (agenticOsDir != null && Path.GetFileName(agenticOsDir) == FileProtocolPaths.AgenticOsDir)
            return agenticOsDir;

        // 如果路径是项目根目录，检查下面有没有 .agentic-os/
        var candidateDir = Path.Combine(storePath, FileProtocolPaths.AgenticOsDir);
        if (Directory.Exists(candidateDir))
            return candidateDir;

        // 回退：尝试向上查找 .agentic-os/
        var current = storePath;
        while (!string.IsNullOrEmpty(current))
        {
            var candidate = Path.Combine(current, FileProtocolPaths.AgenticOsDir);
            if (Directory.Exists(candidate))
                return candidate;

            var parent = Path.GetDirectoryName(current);
            if (parent == current) break;
            current = parent;
        }

        // 最后回退：假设 storePath 本身可作为 agenticOsPath
        return storePath;
    }
}
