using Dna.Knowledge.FileProtocol;
using Dna.Knowledge.FileProtocol.Models;
using Dna.Knowledge.TopoGraph.Models.Nodes;
using Xunit;

namespace App.Tests.FileProtocol;

public sealed class KnowledgeFileStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _agenticOsPath;
    private readonly KnowledgeFileStore _store;

    public KnowledgeFileStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"fp-test-{Guid.NewGuid():N}");
        _agenticOsPath = _tempDir;
        _store = new KnowledgeFileStore();

        // 创建最小知识空间
        CreateSampleModules();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void LoadModules_ShouldReturnAllModules()
    {
        var modules = _store.LoadModules(_agenticOsPath);

        Assert.Equal(3, modules.Count);
        Assert.Contains(modules, m => m.Uid == "Root");
        Assert.Contains(modules, m => m.Uid == "Root/Dept");
        Assert.Contains(modules, m => m.Uid == "Root/Dept/Core");
    }

    [Fact]
    public void LoadModule_ShouldReturnSingleModule()
    {
        var module = _store.LoadModule(_agenticOsPath, "Root/Dept/Core");

        Assert.NotNull(module);
        Assert.Equal("Root/Dept/Core", module.Uid);
        Assert.Equal("Core", module.Name);
        Assert.Equal(TopologyNodeKind.Technical, module.Type);
        Assert.Equal("Root/Dept", module.Parent);
    }

    [Fact]
    public void LoadModule_NotFound_ShouldReturnNull()
    {
        var module = _store.LoadModule(_agenticOsPath, "NonExistent");
        Assert.Null(module);
    }

    [Fact]
    public void LoadIdentity_ShouldReturnMarkdown()
    {
        var identity = _store.LoadIdentity(_agenticOsPath, "Root");
        Assert.NotNull(identity);
        Assert.Contains("## Summary", identity);
    }

    [Fact]
    public void LoadDependencies_ShouldReturnDeps()
    {
        var deps = _store.LoadDependencies(_agenticOsPath, "Root/Dept/Core");
        Assert.Empty(deps);
    }

    [Fact]
    public void LoadDependencies_WithDeps_ShouldParseCorrectly()
    {
        // 添加带依赖的模块
        var appModule = new ModuleFile
        {
            Uid = "Root/Dept/App",
            Name = "App",
            Type = TopologyNodeKind.Team,
            Parent = "Root/Dept",
        };
        var deps = new List<DependencyEntry>
        {
            new() { Target = "Root/Dept/Core", Type = "Association", Note = "test" }
        };
        _store.SaveModule(_agenticOsPath, appModule, null, deps);

        var loaded = _store.LoadDependencies(_agenticOsPath, "Root/Dept/App");
        Assert.Single(loaded);
        Assert.Equal("Root/Dept/Core", loaded[0].Target);
        Assert.Equal("Association", loaded[0].Type);
    }

    [Fact]
    public void SaveAndLoad_Roundtrip_ShouldBeConsistent()
    {
        var module = new ModuleFile
        {
            Uid = "Root/Dept/New",
            Name = "NewModule",
            Type = TopologyNodeKind.Technical,
            Parent = "Root/Dept",
            Keywords = ["bravo", "alpha"],
            Maintainer = "tester",
            CapabilityTags = ["z-cap", "a-cap"]
        };
        var identity = "## Summary\n\nA test module.\n\n## Contract\n\n- IFoo.Bar()\n";
        var deps = new List<DependencyEntry>
        {
            new() { Target = "Root/Dept/Core", Type = "Association" }
        };

        _store.SaveModule(_agenticOsPath, module, identity, deps);

        // 读回
        var loaded = _store.LoadModule(_agenticOsPath, "Root/Dept/New");
        Assert.NotNull(loaded);
        Assert.Equal("NewModule", loaded.Name);
        Assert.Equal(TopologyNodeKind.Technical, loaded.Type);
        Assert.Equal("Root/Dept", loaded.Parent);
        Assert.Equal("tester", loaded.Maintainer);

        // 数组应按字母序
        Assert.Equal(new[] { "alpha", "bravo" }, loaded.Keywords);
        Assert.Equal(new[] { "a-cap", "z-cap" }, loaded.CapabilityTags);

        // Identity
        var loadedIdentity = _store.LoadIdentity(_agenticOsPath, "Root/Dept/New");
        Assert.Contains("A test module", loadedIdentity);

        // Dependencies
        var loadedDeps = _store.LoadDependencies(_agenticOsPath, "Root/Dept/New");
        Assert.Single(loadedDeps);
        Assert.Equal("Root/Dept/Core", loadedDeps[0].Target);
    }

    [Fact]
    public void LoadAsDefinition_ShouldConvertToTopologyModelDefinition()
    {
        var definition = _store.LoadAsDefinition(_agenticOsPath);

        Assert.NotNull(definition.Project);
        Assert.Equal("Root", definition.Project.Id);
        Assert.Single(definition.Departments);
        Assert.Equal("Root/Dept", definition.Departments[0].Id);
        Assert.Single(definition.TechnicalNodes);
        Assert.Equal("Root/Dept/Core", definition.TechnicalNodes[0].Id);
    }

    [Fact]
    public void LoadAsDefinition_ShouldExtractSummaryFromIdentity()
    {
        var definition = _store.LoadAsDefinition(_agenticOsPath);

        Assert.NotNull(definition.Project);
        Assert.Equal("Test project root.", definition.Project.Summary);
    }

    private void CreateSampleModules()
    {
        // Root (Project)
        _store.SaveModule(_agenticOsPath, new ModuleFile
        {
            Uid = "Root",
            Name = "TestProject",
            Type = TopologyNodeKind.Project,
        }, "## Summary\n\nTest project root.\n");

        // Root/Dept (Department)
        _store.SaveModule(_agenticOsPath, new ModuleFile
        {
            Uid = "Root/Dept",
            Name = "Engineering",
            Type = TopologyNodeKind.Department,
            Parent = "Root",
            DisciplineCode = "eng"
        }, "## Summary\n\nEngineering department.\n");

        // Root/Dept/Core (Technical)
        _store.SaveModule(_agenticOsPath, new ModuleFile
        {
            Uid = "Root/Dept/Core",
            Name = "Core",
            Type = TopologyNodeKind.Technical,
            Parent = "Root/Dept",
            Keywords = ["core", "framework"],
            Maintainer = "tester"
        }, "## Summary\n\nCore framework.\n\n## Contract\n\n- ICoreService.Init()\n");
    }
}
