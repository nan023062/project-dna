using Dna.Knowledge.FileProtocol;
using Dna.Knowledge.FileProtocol.Models;
using Xunit;

namespace App.Tests.FileProtocol;

public sealed class SessionFileStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SessionFileStore _store;

    public SessionFileStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"fp-session-test-{Guid.NewGuid():N}");
        _store = new SessionFileStore();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void SaveAndLoad_Roundtrip_ShouldPreserveFields()
    {
        var session = new SessionFile
        {
            Id = "01JSESSIONTEST0000000000000",
            Type = "Working",
            Source = "Ai",
            NodeId = "AgenticOs/Program/App",
            Tags = ["#active-task", "#wip"],
            CreatedAt = new DateTime(2026, 4, 3, 10, 0, 0, DateTimeKind.Utc),
            Body = "# Task\n\nWire session storage.",
            Category = FileProtocolPaths.TasksDir
        };

        _store.SaveSession(_tempDir, session);

        var loaded = _store.LoadSessions(_tempDir);
        var item = Assert.Single(loaded);
        Assert.Equal(session.Id, item.Id);
        Assert.Equal(session.Type, item.Type);
        Assert.Equal(session.Source, item.Source);
        Assert.Equal(session.NodeId, item.NodeId);
        Assert.Equal(session.CreatedAt, item.CreatedAt);
        Assert.Equal(FileProtocolPaths.TasksDir, item.Category);
        Assert.Contains("#active-task", item.Tags!);
        Assert.Contains("Wire session storage.", item.Body);
    }
}
