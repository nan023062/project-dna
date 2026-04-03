using System.Text.Json;
using Dna.Knowledge.FileProtocol;
using Dna.Knowledge.FileProtocol.Models;
using Dna.Knowledge.TopoGraph.Models.Nodes;
using Xunit;

namespace App.Tests.FileProtocol;

public sealed class MetadataRepairToolTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _projectRoot;
    private readonly string _agenticOsPath;
    private readonly MetadataRepairTool _tool;

    public MetadataRepairToolTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"fp-repair-{Guid.NewGuid():N}");
        _projectRoot = _tempDir;
        _agenticOsPath = Path.Combine(_tempDir, ".agentic-os");
        _tool = new MetadataRepairTool();

        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void DryRun_HealthyProject_ShouldReportNoErrors()
    {
        SetupHealthyProject();

        var report = _tool.DryRun(_projectRoot, _agenticOsPath);

        Assert.False(report.HasErrors);
    }

    [Fact]
    public void DryRun_MissingMeta_ShouldDetect()
    {
        // 创建目录但不放 .agentic.meta
        Directory.CreateDirectory(Path.Combine(_projectRoot, "src"));
        SetupMinimalKnowledge();

        var report = _tool.DryRun(_projectRoot, _agenticOsPath);

        Assert.Contains(report.Issues, i => i.Type == "missing-meta" && i.DirectoryPath == "src");
    }

    [Fact]
    public void DryRun_InvalidMeta_ShouldDetect()
    {
        var srcDir = Path.Combine(_projectRoot, "src");
        Directory.CreateDirectory(srcDir);
        File.WriteAllText(Path.Combine(srcDir, ".agentic.meta"), "not valid json");
        SetupMinimalKnowledge();

        var report = _tool.DryRun(_projectRoot, _agenticOsPath);

        Assert.Contains(report.Issues, i => i.Type == "invalid-meta");
    }

    [Fact]
    public void DryRun_DanglingReference_ShouldDetect()
    {
        SetupMinimalKnowledge();
        // 模块引用一个不存在的 GUID
        var store = new KnowledgeFileStore();
        store.SaveModule(_agenticOsPath, new ModuleFile
        {
            Uid = "Root",
            Name = "Root",
            Type = TopologyNodeKind.Project,
            ManagedPaths = ["deadbeefdeadbeefdeadbeefdeadbeef"]
        });

        var report = _tool.DryRun(_projectRoot, _agenticOsPath);

        Assert.Contains(report.Issues, i => i.Type == "dangling-reference");
    }

    [Fact]
    public void DryRun_DuplicateGuid_ShouldDetect()
    {
        var sameGuid = Guid.NewGuid().ToString("N");

        CreateMetaFile(Path.Combine(_projectRoot, "dirA"), sameGuid);
        CreateMetaFile(Path.Combine(_projectRoot, "dirB"), sameGuid);
        SetupMinimalKnowledge();

        var report = _tool.DryRun(_projectRoot, _agenticOsPath);

        Assert.Contains(report.Issues, i => i.Type == "duplicate-guid");
    }

    [Fact]
    public void DryRun_OrphanMeta_ShouldReport()
    {
        var guid = Guid.NewGuid().ToString("N");
        CreateMetaFile(Path.Combine(_projectRoot, "orphan-dir"), guid);

        SetupMinimalKnowledge(); // 模块不引用这个 guid

        var report = _tool.DryRun(_projectRoot, _agenticOsPath);

        Assert.Contains(report.Issues, i => i.Type == "orphan-meta");
    }

    [Fact]
    public void Apply_MissingMeta_ShouldCreateFile()
    {
        var srcDir = Path.Combine(_projectRoot, "src");
        Directory.CreateDirectory(srcDir);
        SetupMinimalKnowledge();

        var metaPath = Path.Combine(srcDir, ".agentic.meta");
        Assert.False(File.Exists(metaPath));

        _tool.Apply(_projectRoot, _agenticOsPath);

        Assert.True(File.Exists(metaPath));
    }

    [Fact]
    public void Apply_InvalidMeta_ShouldRegenerateWithBackup()
    {
        var srcDir = Path.Combine(_projectRoot, "src");
        Directory.CreateDirectory(srcDir);
        var metaPath = Path.Combine(srcDir, ".agentic.meta");
        File.WriteAllText(metaPath, "broken");
        SetupMinimalKnowledge();

        var report = _tool.Apply(_projectRoot, _agenticOsPath);

        // 应该有备份
        Assert.NotEmpty(report.BackedUpFiles);
        // 新文件应该可解析
        var json = File.ReadAllText(metaPath);
        Assert.Contains("stableGuid", json);
    }

    [Fact]
    public void Apply_DanglingReference_ShouldNotAutoFix()
    {
        // 给所有目录先补上 meta，避免 missing-meta 修复干扰
        CreateMetaFile(_projectRoot, Guid.NewGuid().ToString("N"));

        var store = new KnowledgeFileStore();
        store.SaveModule(_agenticOsPath, new ModuleFile
        {
            Uid = "Root",
            Name = "Root",
            Type = TopologyNodeKind.Project,
            ManagedPaths = ["deadbeefdeadbeefdeadbeefdeadbeef"]
        });

        var report = _tool.Apply(_projectRoot, _agenticOsPath);

        // 悬空引用不应自动修复（类型为 manual-confirm）
        Assert.DoesNotContain(report.AppliedFixes, f => f.Contains("deadbeef"));
        Assert.Contains(report.Issues, i => i.Type == "dangling-reference");
    }

    [Fact]
    public void FormatReport_ShouldProduceReadableOutput()
    {
        SetupMinimalKnowledge();
        var report = _tool.DryRun(_projectRoot, _agenticOsPath);
        var text = MetadataRepairTool.FormatReport(report);

        Assert.Contains("Metadata Repair Report", text);
        Assert.Contains("扫描目录", text);
    }

    // ============== Helpers ==============

    private void SetupHealthyProject()
    {
        // 创建目录 + meta
        var guid = Guid.NewGuid().ToString("N");
        CreateMetaFile(_projectRoot, Guid.NewGuid().ToString("N"));

        var srcDir = Path.Combine(_projectRoot, "src");
        CreateMetaFile(srcDir, guid);

        // 创建引用该 guid 的模块
        var store = new KnowledgeFileStore();
        store.SaveModule(_agenticOsPath, new ModuleFile
        {
            Uid = "Root",
            Name = "Root",
            Type = TopologyNodeKind.Project,
            ManagedPaths = [guid]
        });
    }

    private void SetupMinimalKnowledge()
    {
        var modulesRoot = FileProtocolPaths.GetModulesRoot(_agenticOsPath);
        if (Directory.Exists(modulesRoot)) return;

        var store = new KnowledgeFileStore();
        store.SaveModule(_agenticOsPath, new ModuleFile
        {
            Uid = "Root",
            Name = "Root",
            Type = TopologyNodeKind.Project,
        });
    }

    private static void CreateMetaFile(string dirPath, string stableGuid)
    {
        Directory.CreateDirectory(dirPath);
        var doc = new { schema = "agentic-os/workspace-directory/v1", stableGuid, updatedAtUtc = DateTime.UtcNow };
        var json = JsonSerializer.Serialize(doc, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        File.WriteAllText(Path.Combine(dirPath, ".agentic.meta"), json);
    }
}
