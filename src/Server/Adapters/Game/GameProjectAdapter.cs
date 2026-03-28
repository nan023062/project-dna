using Dna.Knowledge;

namespace Dna.Adapters.Game;

public class GameProjectAdapter : IProjectAdapter
{
    public string ProjectType => "Game";

    private readonly Dictionary<string, IContextInterpreter> _interpreters;

    public GameProjectAdapter()
    {
        _interpreters = new Dictionary<string, IContextInterpreter>(StringComparer.OrdinalIgnoreCase)
        {
            { "coder", new CoderInterpreter() },
            { "designer", new DesignerInterpreter() },
            { "art", new ArtInterpreter() }
        };
    }

    public IEnumerable<RoleDefinition> GetRoles()
    {
        yield return new RoleDefinition { Id = "coder", Name = "程序", Description = "负责游戏逻辑与系统开发" };
        yield return new RoleDefinition { Id = "designer", Name = "策划", Description = "负责玩法设计与数值配置" };
        yield return new RoleDefinition { Id = "art", Name = "美术", Description = "负责视觉资产与动画" };
    }

    public IEnumerable<CollaborationRule> GetRoleCollaborationRules()
    {
        yield return new CollaborationRule { SourceRoleId = "coder", TargetRoleId = "designer", Relationship = "DependsOn", Description = "程序依赖策划的设计文档和配置表" };
        yield return new CollaborationRule { SourceRoleId = "coder", TargetRoleId = "art", Relationship = "DependsOn", Description = "程序依赖美术提供的资产" };
        yield return new CollaborationRule { SourceRoleId = "designer", TargetRoleId = "art", Relationship = "Symbiotic", Description = "策划与美术在表现上共生" };
    }

    public IContextInterpreter GetInterpreter(string roleId)
    {
        return _interpreters.TryGetValue(roleId, out var interpreter)
            ? interpreter
            : _interpreters["coder"];
    }

    public List<string> GetModuleFiles(string relativePath)
    {
        // TODO: 结合 ProjectRoot 扫描实际文件
        return [];
    }

    public List<string> ComputeDependencies(KnowledgeNode module, List<KnowledgeNode> allModules)
    {
        return module.Discipline?.ToLowerInvariant() switch
        {
            "coder" => ComputeCoderDependencies(module, allModules),
            "art" => ComputeArtDependencies(module, allModules),
            "designer" => ComputeDesignerDependencies(module, allModules),
            _ => []
        };
    }

    private List<string> ComputeCoderDependencies(KnowledgeNode module, List<KnowledgeNode> allModules)
    {
        // TODO: 扫描 C# 文件中的 using 语句，或者解析 .csproj 中的 ProjectReference
        return [];
    }

    private List<string> ComputeArtDependencies(KnowledgeNode module, List<KnowledgeNode> allModules)
    {
        // TODO: 扫描 .prefab 或 .mat 等资产文件中的 GUID 引用
        return [];
    }

    private List<string> ComputeDesignerDependencies(KnowledgeNode module, List<KnowledgeNode> allModules)
    {
        // TODO: 扫描配置表 (JSON/Excel) 中的数据外键 (Foreign Keys)
        return [];
    }

    public AdapterValidationResult ValidateContract(CrossWorkParticipant participant, KnowledgeNode module)
    {
        if (string.IsNullOrWhiteSpace(participant.ContractType))
            return AdapterValidationResult.Success(); // 没有类型，无法强校验，默认通过

        return participant.ContractType.ToLowerInvariant() switch
        {
            "codeinterface" => ValidateCodeInterface(participant, module),
            "assetsocket" => ValidateAssetSocket(participant, module),
            "dataforeignkey" => ValidateDataForeignKey(participant, module),
            _ => AdapterValidationResult.Success()
        };
    }

    private AdapterValidationResult ValidateCodeInterface(CrossWorkParticipant participant, KnowledgeNode module)
    {
        // TODO: 扫描 C# 代码，检查是否真的实现了 participant.Contract 中声明的接口或事件
        return AdapterValidationResult.Success();
    }

    private AdapterValidationResult ValidateAssetSocket(CrossWorkParticipant participant, KnowledgeNode module)
    {
        // TODO: 扫描 FBX/Prefab，检查是否真的存在 participant.Contract 中声明的挂载点
        return AdapterValidationResult.Success();
    }

    private AdapterValidationResult ValidateDataForeignKey(CrossWorkParticipant participant, KnowledgeNode module)
    {
        // TODO: 扫描 JSON/Excel，检查是否真的存在 participant.Contract 中声明的字段
        return AdapterValidationResult.Success();
    }
}
