
namespace Dna.Knowledge;

/// <summary>
/// 项目角色定义
/// </summary>
public class RoleDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
}

/// <summary>
/// 协作规则
/// </summary>
public class CollaborationRule
{
    public required string SourceRoleId { get; init; }
    public required string TargetRoleId { get; init; }
    public required string Relationship { get; init; }
    public string? Description { get; init; }
}

/// <summary>
/// 适配器校验结果
/// </summary>
public class AdapterValidationResult
{
    public bool IsValid { get; init; }
    public List<string> Errors { get; init; } = [];

    public static AdapterValidationResult Success() => new() { IsValid = true };
    public static AdapterValidationResult Failure(IEnumerable<string> errors) =>
        new() { IsValid = false, Errors = new List<string>(errors) };
}

/// <summary>
/// 模块上下文解释器 — 将模块上下文按角色差异化解释为 AI Prompt。
/// </summary>
public interface IContextInterpreter
{
    string RoleId { get; }

    Dictionary<string, string> GetTemplates();

    string InterpretContext(ModuleContext context);

    AdapterValidationResult ValidateContext(ModuleContext context);
}

/// <summary>
/// 项目类型适配器 — 不同项目类型提供不同的角色解释器和协作规则。
/// </summary>
public interface IProjectAdapter
{
    string ProjectType { get; }

    IContextInterpreter GetInterpreter(string roleId);

    IEnumerable<RoleDefinition> GetRoles();

    IEnumerable<CollaborationRule> GetRoleCollaborationRules();

    /// <summary>
    /// 获取模块目录下的所有文件路径（相对路径）
    /// </summary>
    List<string> GetModuleFiles(string relativePath);

    /// <summary>
    /// 推导模块的实际依赖关系（事实依赖）
    /// </summary>
    List<string> ComputeDependencies(KnowledgeNode module, List<KnowledgeNode> allModules);

    /// <summary>
    /// 校验 CrossWork 契约的实现情况（强校验）
    /// </summary>
    AdapterValidationResult ValidateContract(CrossWorkParticipant participant, KnowledgeNode module);
}
