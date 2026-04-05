using Dna.Knowledge.FileProtocol;
using Dna.Knowledge.FileProtocol.Models;
using Xunit;

namespace App.Tests.FileProtocol;

public sealed class MemoryFileStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _agenticOsPath;
    private readonly MemoryFileStore _store;

    public MemoryFileStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"fp-mem-test-{Guid.NewGuid():N}");
        _agenticOsPath = _tempDir;
        _store = new MemoryFileStore();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void SaveAndLoad_Roundtrip_ShouldPreserveFields()
    {
        var memory = new MemoryFile
        {
            Id = "01JX8TEST0000000000000000",
            Type = "Semantic",
            Source = "Human",
            NodeId = "Root/Dept/Core",
            Disciplines = ["engineering"],
            Tags = ["decision", "architecture"],
            Importance = 0.8,
            CreatedAt = new DateTime(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc),
            Body = "# Test Decision\n\nThis is a test decision.",
            Category = "decisions"
        };

        _store.SaveMemory(_agenticOsPath, memory);

        // 读回
        var loaded = _store.LoadMemories(_agenticOsPath);
        Assert.Single(loaded);

        var m = loaded[0];
        Assert.Equal("01JX8TEST0000000000000000", m.Id);
        Assert.Equal("Semantic", m.Type);
        Assert.Equal("Human", m.Source);
        Assert.Equal("Root/Dept/Core", m.NodeId);
        Assert.Contains("engineering", m.Disciplines!);
        Assert.Contains("architecture", m.Tags!);
        Assert.Contains("decision", m.Tags!);
        Assert.Equal(0.8, m.Importance);
        Assert.Contains("Test Decision", m.Body);
        Assert.Equal("decisions", m.Category);
    }

    [Fact]
    public void LoadMemories_EmptyDir_ShouldReturnEmpty()
    {
        var result = _store.LoadMemories(_agenticOsPath);
        Assert.Empty(result);
    }

    [Fact]
    public void SaveMemory_ShouldInferCategory_FromType()
    {
        _store.SaveMemory(_agenticOsPath, new MemoryFile
        {
            Id = "01JX8TEST0000000000000001",
            Type = "Episodic",
            Source = "Ai",
            CreatedAt = DateTime.UtcNow,
            Body = "# Lesson\n\nA lesson learned."
        });

        // Episodic → lessons/
        var lessonsDir = Path.Combine(_agenticOsPath, "memory", "lessons");
        Assert.True(Directory.Exists(lessonsDir));
        Assert.Single(Directory.GetFiles(lessonsDir, "*.md"));
    }

    [Fact]
    public void SaveMemory_Procedural_ShouldGoToConventions()
    {
        _store.SaveMemory(_agenticOsPath, new MemoryFile
        {
            Id = "01JX8TEST0000000000000002",
            Type = "Procedural",
            Source = "Human",
            CreatedAt = DateTime.UtcNow,
            Body = "# Convention\n\nA coding convention."
        });

        var conventionsDir = Path.Combine(_agenticOsPath, "memory", "conventions");
        Assert.True(Directory.Exists(conventionsDir));
        Assert.Single(Directory.GetFiles(conventionsDir, "*.md"));
    }

    [Fact]
    public void LoadMemory_SingleFile_ShouldParseFrontmatter()
    {
        var content = """
            ---
            id: 01JX8SINGLE00000000000000
            type: Structural
            source: System
            nodeId: Root
            disciplines:
              - engineering
              - design
            tags:
              - topology
            importance: 0.9
            createdAt: 2026-04-01T12:00:00Z
            ---

            # Topology Update

            Module structure changed.
            """;

        var dir = Path.Combine(_agenticOsPath, "memory", "decisions");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "01JX8SINGLE00000000000000.md"), content);

        var loaded = _store.LoadMemory(Path.Combine(dir, "01JX8SINGLE00000000000000.md"));
        Assert.NotNull(loaded);
        Assert.Equal("01JX8SINGLE00000000000000", loaded.Id);
        Assert.Equal("Structural", loaded.Type);
        Assert.Equal("System", loaded.Source);
        Assert.Equal("Root", loaded.NodeId);
        Assert.Equal(0.9, loaded.Importance);
        Assert.Contains("engineering", loaded.Disciplines!);
        Assert.Contains("design", loaded.Disciplines!);
        Assert.Contains("topology", loaded.Tags!);
        Assert.Contains("Topology Update", loaded.Body);
    }

    [Fact]
    public void LoadMemories_MultipleCategories_ShouldLoadAll()
    {
        _store.SaveMemory(_agenticOsPath, new MemoryFile
        {
            Id = "01JX8MULTI00000000000001",
            Type = "Semantic",
            Source = "Human",
            CreatedAt = DateTime.UtcNow,
            Body = "Decision",
            Category = "decisions"
        });
        _store.SaveMemory(_agenticOsPath, new MemoryFile
        {
            Id = "01JX8MULTI00000000000002",
            Type = "Episodic",
            Source = "Human",
            CreatedAt = DateTime.UtcNow,
            Body = "Lesson",
            Category = "lessons"
        });
        _store.SaveMemory(_agenticOsPath, new MemoryFile
        {
            Id = "01JX8MULTI00000000000003",
            Type = "Procedural",
            Source = "Human",
            CreatedAt = DateTime.UtcNow,
            Body = "Convention",
            Category = "conventions"
        });

        var all = _store.LoadMemories(_agenticOsPath);
        Assert.Equal(3, all.Count);
    }
}
