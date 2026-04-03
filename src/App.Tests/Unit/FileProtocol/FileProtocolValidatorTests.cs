using Dna.Knowledge.FileProtocol;
using Dna.Knowledge.FileProtocol.Models;
using Dna.Knowledge.TopoGraph.Models.Nodes;
using Xunit;

namespace App.Tests.FileProtocol;

public sealed class FileProtocolValidatorTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _agenticOsPath;
    private readonly KnowledgeFileStore _store;
    private readonly FileProtocolValidator _validator;

    public FileProtocolValidatorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"fp-val-test-{Guid.NewGuid():N}");
        _agenticOsPath = _tempDir;
        _store = new KnowledgeFileStore();
        _validator = new FileProtocolValidator();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Validate_ValidStructure_ShouldPass()
    {
        CreateValidStructure();

        var result = _validator.Validate(_agenticOsPath);

        Assert.True(result.IsValid, string.Join("\n", result.Errors));
    }

    [Fact]
    public void Validate_MissingParent_ShouldFail()
    {
        _store.SaveModule(_agenticOsPath, new ModuleFile
        {
            Uid = "Root",
            Name = "Root",
            Type = TopologyNodeKind.Project,
        });
        _store.SaveModule(_agenticOsPath, new ModuleFile
        {
            Uid = "Root/Dept/Orphan",
            Name = "Orphan",
            Type = TopologyNodeKind.Technical,
            Parent = "Root/NonExistent"
        });

        var result = _validator.Validate(_agenticOsPath);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("parent 引用不存在"));
    }

    [Fact]
    public void Validate_InvalidParentChildType_ShouldFail()
    {
        _store.SaveModule(_agenticOsPath, new ModuleFile
        {
            Uid = "Root",
            Name = "Root",
            Type = TopologyNodeKind.Project,
        });
        // Technical 直接挂 Project 下（应该只允许 Department）
        _store.SaveModule(_agenticOsPath, new ModuleFile
        {
            Uid = "Root/Core",
            Name = "Core",
            Type = TopologyNodeKind.Technical,
            Parent = "Root"
        });

        var result = _validator.Validate(_agenticOsPath);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("不允许包含"));
    }

    [Fact]
    public void Validate_ProjectWithParent_ShouldFail()
    {
        _store.SaveModule(_agenticOsPath, new ModuleFile
        {
            Uid = "Root",
            Name = "Root",
            Type = TopologyNodeKind.Project,
            Parent = "SomeParent"
        });

        var result = _validator.Validate(_agenticOsPath);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Project 类型不应有 parent"));
    }

    [Fact]
    public void Validate_MissingDependencyTarget_ShouldFail()
    {
        CreateValidStructure();

        // 添加一个引用不存在模块的依赖
        _store.SaveModule(_agenticOsPath, new ModuleFile
        {
            Uid = "Root/Dept/Bad",
            Name = "Bad",
            Type = TopologyNodeKind.Technical,
            Parent = "Root/Dept"
        }, null, [new DependencyEntry { Target = "Root/Dept/NonExistent" }]);

        var result = _validator.Validate(_agenticOsPath);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("依赖目标不存在"));
    }

    [Fact]
    public void Validate_DependingOnTeam_ShouldWarn()
    {
        _store.SaveModule(_agenticOsPath, new ModuleFile
        {
            Uid = "Root",
            Name = "Root",
            Type = TopologyNodeKind.Project,
        });
        _store.SaveModule(_agenticOsPath, new ModuleFile
        {
            Uid = "Root/Dept",
            Name = "Dept",
            Type = TopologyNodeKind.Department,
            Parent = "Root"
        });
        _store.SaveModule(_agenticOsPath, new ModuleFile
        {
            Uid = "Root/Dept/TeamA",
            Name = "TeamA",
            Type = TopologyNodeKind.Team,
            Parent = "Root/Dept"
        });
        _store.SaveModule(_agenticOsPath, new ModuleFile
        {
            Uid = "Root/Dept/Core",
            Name = "Core",
            Type = TopologyNodeKind.Technical,
            Parent = "Root/Dept"
        }, null, [new DependencyEntry { Target = "Root/Dept/TeamA" }]);

        var result = _validator.Validate(_agenticOsPath);

        Assert.Contains(result.Warnings, w => w.Contains("Team 不应被其他模块依赖"));
    }

    [Fact]
    public void Validate_CyclicParent_ShouldFail()
    {
        // 手动创建循环父子关系
        var modulesRoot = FileProtocolPaths.GetModulesRoot(_agenticOsPath);

        CreateModuleJson(modulesRoot, "A", new ModuleFile
        {
            Uid = "A",
            Name = "A",
            Type = TopologyNodeKind.Department,
            Parent = "B"
        });
        CreateModuleJson(modulesRoot, "B", new ModuleFile
        {
            Uid = "B",
            Name = "B",
            Type = TopologyNodeKind.Department,
            Parent = "A"
        });

        var result = _validator.Validate(_agenticOsPath);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("循环父子关系"));
    }

    [Fact]
    public void Validate_InvalidStableGuid_ShouldWarn()
    {
        _store.SaveModule(_agenticOsPath, new ModuleFile
        {
            Uid = "Root",
            Name = "Root",
            Type = TopologyNodeKind.Project,
            ManagedPaths = ["not-a-valid-guid"]
        });

        var result = _validator.Validate(_agenticOsPath);

        Assert.Contains(result.Warnings, w => w.Contains("StableGuid"));
    }

    [Fact]
    public void Validate_EmptyKnowledgeSpace_ShouldWarn()
    {
        Directory.CreateDirectory(FileProtocolPaths.GetModulesRoot(_agenticOsPath));

        var result = _validator.Validate(_agenticOsPath);

        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, w => w.Contains("未找到任何模块定义"));
    }

    private void CreateValidStructure()
    {
        _store.SaveModule(_agenticOsPath, new ModuleFile
        {
            Uid = "Root",
            Name = "Root",
            Type = TopologyNodeKind.Project,
        });
        _store.SaveModule(_agenticOsPath, new ModuleFile
        {
            Uid = "Root/Dept",
            Name = "Dept",
            Type = TopologyNodeKind.Department,
            Parent = "Root",
            DisciplineCode = "eng"
        });
        _store.SaveModule(_agenticOsPath, new ModuleFile
        {
            Uid = "Root/Dept/Core",
            Name = "Core",
            Type = TopologyNodeKind.Technical,
            Parent = "Root/Dept"
        });
    }

    private static void CreateModuleJson(string modulesRoot, string uid, ModuleFile module)
    {
        var dir = Path.Combine(modulesRoot, uid.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(dir);
        var json = System.Text.Json.JsonSerializer.Serialize(module, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });
        File.WriteAllText(Path.Combine(dir, "module.json"), json);
    }
}
