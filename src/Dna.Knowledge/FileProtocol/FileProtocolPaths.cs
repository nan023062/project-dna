namespace Dna.Knowledge.FileProtocol;

/// <summary>
/// .agentic-os/ 文件协议的路径约定与常量。
/// </summary>
public static class FileProtocolPaths
{
    public const string AgenticOsDir = ".agentic-os";
    public const string KnowledgeDir = "knowledge";
    public const string ModulesDir = "modules";
    public const string MemoryDir = "memory";
    public const string SessionDir = "session";

    public const string ModuleFileName = "module.json";
    public const string IdentityFileName = "identity.md";
    public const string DependenciesFileName = "dependencies.json";

    // Memory 分类目录
    public const string DecisionsDir = "decisions";
    public const string LessonsDir = "lessons";
    public const string ConventionsDir = "conventions";
    public const string SummariesDir = "summaries";

    // Session 分类目录
    public const string TasksDir = "tasks";
    public const string ContextDir = "context";

    /// <summary>获取 knowledge/modules/ 根路径</summary>
    public static string GetModulesRoot(string agenticOsPath)
        => Path.Combine(agenticOsPath, KnowledgeDir, ModulesDir);

    /// <summary>根据模块 UID 获取模块目录路径</summary>
    public static string GetModuleDir(string agenticOsPath, string uid)
        => Path.Combine(GetModulesRoot(agenticOsPath), uid.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>获取 module.json 路径</summary>
    public static string GetModuleFilePath(string agenticOsPath, string uid)
        => Path.Combine(GetModuleDir(agenticOsPath, uid), ModuleFileName);

    /// <summary>获取 identity.md 路径</summary>
    public static string GetIdentityFilePath(string agenticOsPath, string uid)
        => Path.Combine(GetModuleDir(agenticOsPath, uid), IdentityFileName);

    /// <summary>获取 dependencies.json 路径</summary>
    public static string GetDependenciesFilePath(string agenticOsPath, string uid)
        => Path.Combine(GetModuleDir(agenticOsPath, uid), DependenciesFileName);

    /// <summary>获取 memory/ 根路径</summary>
    public static string GetMemoryRoot(string agenticOsPath)
        => Path.Combine(agenticOsPath, MemoryDir);

    /// <summary>获取 memory 分类目录路径</summary>
    public static string GetMemoryCategoryDir(string agenticOsPath, string category)
        => Path.Combine(GetMemoryRoot(agenticOsPath), category);

    /// <summary>获取 session/ 根路径</summary>
    public static string GetSessionRoot(string agenticOsPath)
        => Path.Combine(agenticOsPath, SessionDir);

    /// <summary>
    /// 从模块目录的绝对路径反推 UID。
    /// 例如 /project/.agentic-os/knowledge/modules/ProjectDna/Program/DnaCore → ProjectDna/Program/DnaCore
    /// </summary>
    public static string? ExtractUidFromPath(string agenticOsPath, string moduleDir)
    {
        var modulesRoot = GetModulesRoot(agenticOsPath);
        var normalized = moduleDir.Replace('\\', '/').TrimEnd('/');
        var root = modulesRoot.Replace('\\', '/').TrimEnd('/');

        if (!normalized.StartsWith(root, StringComparison.Ordinal))
            return null;

        var relative = normalized[(root.Length + 1)..];
        return relative.Replace('\\', '/');
    }
}
