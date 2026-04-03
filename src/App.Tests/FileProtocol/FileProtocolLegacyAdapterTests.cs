using Dna.Knowledge.FileProtocol;
using Dna.Knowledge.FileProtocol.Models;
using Dna.Knowledge.TopoGraph.Models.Nodes;
using Xunit;

namespace App.Tests.FileProtocol;

public sealed class FileProtocolLegacyAdapterTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _agenticOsPath;

    public FileProtocolLegacyAdapterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"fp-legacy-test-{Guid.NewGuid():N}");
        _agenticOsPath = Path.Combine(_tempDir, ".agentic-os");

        CreateSampleKnowledgeSpace();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void LoadAsLegacyManifests_ShouldReturnArchitecture()
    {
        var (architecture, _) = FileProtocolLegacyAdapter.LoadAsLegacyManifests(_agenticOsPath);

        Assert.NotEmpty(architecture.Disciplines);
        Assert.True(architecture.Disciplines.ContainsKey("eng"));
    }

    [Fact]
    public void LoadAsLegacyManifests_ShouldReturnModules()
    {
        var (_, modules) = FileProtocolLegacyAdapter.LoadAsLegacyManifests(_agenticOsPath);

        Assert.NotEmpty(modules.Disciplines);
        var allModules = modules.Disciplines.Values.SelectMany(x => x).ToList();
        Assert.Equal(4, allModules.Count);
    }

    [Fact]
    public void LoadAsLegacyManifests_ShouldPreserveDependencies()
    {
        var (_, modules) = FileProtocolLegacyAdapter.LoadAsLegacyManifests(_agenticOsPath);

        var allModules = modules.Disciplines.Values.SelectMany(x => x).ToList();
        var app = allModules.First(m => m.Name == "App");
        Assert.Contains("Root/Dept/Core", app.Dependencies);
    }

    [Fact]
    public void LoadAsLegacyManifests_ShouldInferLayerFromType()
    {
        var (_, modules) = FileProtocolLegacyAdapter.LoadAsLegacyManifests(_agenticOsPath);

        var allModules = modules.Disciplines.Values.SelectMany(x => x).ToList();
        var core = allModules.First(m => m.Name == "Core");
        var app = allModules.First(m => m.Name == "App");

        Assert.Equal(1, core.Layer); // Technical → 1
        Assert.Equal(3, app.Layer);  // Team → 3
    }

    [Fact]
    public void LoadAsLegacyManifests_ShouldInferDiscipline()
    {
        var (_, modules) = FileProtocolLegacyAdapter.LoadAsLegacyManifests(_agenticOsPath);

        // Core 和 App 应该归到 eng discipline（从 Department 的 disciplineCode 推导）
        Assert.True(modules.Disciplines.ContainsKey("eng"));
        var engModules = modules.Disciplines["eng"];
        Assert.Contains(engModules, m => m.Name == "Core");
        Assert.Contains(engModules, m => m.Name == "App");
    }

    [Fact]
    public void LoadAsLegacyManifests_EmptyDir_ShouldReturnEmpty()
    {
        var emptyPath = Path.Combine(Path.GetTempPath(), $"fp-empty-{Guid.NewGuid():N}");
        Directory.CreateDirectory(emptyPath);
        try
        {
            var (architecture, modules) = FileProtocolLegacyAdapter.LoadAsLegacyManifests(emptyPath);
            Assert.Empty(architecture.Disciplines);
            Assert.Empty(modules.Disciplines);
        }
        finally
        {
            Directory.Delete(emptyPath, recursive: true);
        }
    }

    private void CreateSampleKnowledgeSpace()
    {
        var fileStore = new KnowledgeFileStore();

        fileStore.SaveModule(_agenticOsPath, new ModuleFile
        {
            Uid = "Root",
            Name = "TestProject",
            Type = TopologyNodeKind.Project,
        });

        fileStore.SaveModule(_agenticOsPath, new ModuleFile
        {
            Uid = "Root/Dept",
            Name = "Engineering",
            Type = TopologyNodeKind.Department,
            Parent = "Root",
            DisciplineCode = "eng"
        });

        fileStore.SaveModule(_agenticOsPath, new ModuleFile
        {
            Uid = "Root/Dept/Core",
            Name = "Core",
            Type = TopologyNodeKind.Technical,
            Parent = "Root/Dept"
        });

        fileStore.SaveModule(_agenticOsPath, new ModuleFile
        {
            Uid = "Root/Dept/App",
            Name = "App",
            Type = TopologyNodeKind.Team,
            Parent = "Root/Dept"
        }, null, [
            new DependencyEntry { Target = "Root/Dept/Core", Type = "Association" }
        ]);
    }
}
