using Dna.Knowledge.FileProtocol;
using Dna.Knowledge.FileProtocol.Models;
using Dna.Knowledge.TopoGraph.Models.Nodes;
using Xunit;

namespace App.Tests.FileProtocol;

public sealed class FileBasedDefinitionStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _agenticOsPath;
    private readonly FileBasedDefinitionStore _store;

    public FileBasedDefinitionStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"fp-def-test-{Guid.NewGuid():N}");
        _agenticOsPath = Path.Combine(_tempDir, ".agentic-os");
        _store = new FileBasedDefinitionStore();

        CreateSampleKnowledgeSpace();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Initialize_ShouldResolveAgenticOsPath()
    {
        _store.Initialize(_agenticOsPath);
        Assert.True(_store.HasKnowledgeFiles());
    }

    [Fact]
    public void Initialize_FromProjectRoot_ShouldFindAgenticOs()
    {
        _store.Initialize(_tempDir);
        Assert.True(_store.HasKnowledgeFiles());
    }

    [Fact]
    public void LoadDefinition_ShouldReturnAllNodes()
    {
        _store.Initialize(_agenticOsPath);
        var definition = _store.LoadDefinition();

        Assert.NotNull(definition.Project);
        Assert.Equal("Root", definition.Project.Id);
        Assert.Single(definition.Departments);
        Assert.Single(definition.TechnicalNodes);
        Assert.Single(definition.TeamNodes);
    }

    [Fact]
    public void LoadDefinition_ShouldPreserveParentRelationships()
    {
        _store.Initialize(_agenticOsPath);
        var definition = _store.LoadDefinition();

        Assert.Equal("Root", definition.Departments[0].ParentId);
        Assert.Equal("Root/Dept", definition.TechnicalNodes[0].ParentId);
        Assert.Equal("Root/Dept", definition.TeamNodes[0].ParentId);
    }

    [Fact]
    public void LoadDefinition_ShouldPreserveDependencies()
    {
        _store.Initialize(_agenticOsPath);
        var definition = _store.LoadDefinition();

        var team = definition.TeamNodes[0];
        Assert.Contains("Root/Dept/Core", team.TechnicalDependencies);
    }

    [Fact]
    public void HasKnowledgeFiles_EmptyDir_ShouldReturnFalse()
    {
        var emptyDir = Path.Combine(Path.GetTempPath(), $"fp-empty-{Guid.NewGuid():N}");
        Directory.CreateDirectory(emptyDir);
        try
        {
            var store = new FileBasedDefinitionStore();
            store.Initialize(emptyDir);
            Assert.False(store.HasKnowledgeFiles());
        }
        finally
        {
            Directory.Delete(emptyDir, recursive: true);
        }
    }

    [Fact]
    public void SaveAndLoad_Roundtrip_ShouldBeConsistent()
    {
        _store.Initialize(_agenticOsPath);
        var original = _store.LoadDefinition();

        // Save back
        _store.SaveDefinition(original);

        // Reload
        _store.Reload();
        var reloaded = _store.LoadDefinition();

        Assert.NotNull(reloaded.Project);
        Assert.Equal(original.Project!.Id, reloaded.Project!.Id);
        Assert.Equal(original.Departments.Count, reloaded.Departments.Count);
        Assert.Equal(original.TechnicalNodes.Count, reloaded.TechnicalNodes.Count);
        Assert.Equal(original.TeamNodes.Count, reloaded.TeamNodes.Count);
    }

    private void CreateSampleKnowledgeSpace()
    {
        var fileStore = new KnowledgeFileStore();

        fileStore.SaveModule(_agenticOsPath, new ModuleFile
        {
            Uid = "Root",
            Name = "TestProject",
            Type = TopologyNodeKind.Project,
        }, "## Summary\n\nTest project.\n");

        fileStore.SaveModule(_agenticOsPath, new ModuleFile
        {
            Uid = "Root/Dept",
            Name = "Engineering",
            Type = TopologyNodeKind.Department,
            Parent = "Root",
            DisciplineCode = "eng"
        }, "## Summary\n\nEngineering.\n");

        fileStore.SaveModule(_agenticOsPath, new ModuleFile
        {
            Uid = "Root/Dept/Core",
            Name = "Core",
            Type = TopologyNodeKind.Technical,
            Parent = "Root/Dept"
        }, "## Summary\n\nCore module.\n");

        fileStore.SaveModule(_agenticOsPath, new ModuleFile
        {
            Uid = "Root/Dept/App",
            Name = "App",
            Type = TopologyNodeKind.Team,
            Parent = "Root/Dept"
        }, "## Summary\n\nApp module.\n", [
            new DependencyEntry { Target = "Root/Dept/Core", Type = "Association" }
        ]);
    }
}
