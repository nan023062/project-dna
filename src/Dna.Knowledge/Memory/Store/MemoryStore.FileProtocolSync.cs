using Dna.Knowledge.FileProtocol;
using Dna.Knowledge.FileProtocol.Models;
using Dna.Memory.Models;

namespace Dna.Memory.Store;

public partial class MemoryStore
{
    private readonly MemoryFileStore _memoryFileStore = new();
    private readonly SessionFileStore _sessionFileStore = new();

    private void SyncEntryToFileProtocol(MemoryEntry entry)
    {
        var agenticOsPath = ResolveAgenticOsPathForMemory(_storePath);
        if (string.IsNullOrWhiteSpace(agenticOsPath))
            return;

        var memoryPaths = GetMemoryFilePaths(agenticOsPath, entry.Id);
        var sessionPaths = GetSessionFilePaths(agenticOsPath, entry.Id);

        if (!ShouldPersistToFileProtocol(entry))
        {
            DeleteFiles(memoryPaths);
            DeleteFiles(sessionPaths);
            return;
        }

        if (entry.Stage == MemoryStage.ShortTerm)
        {
            var sessionFile = ToSessionFile(entry);
            _sessionFileStore.SaveSession(agenticOsPath, sessionFile);
            DeleteUnexpectedFiles(
                sessionPaths,
                Path.Combine(FileProtocolPaths.GetSessionRoot(agenticOsPath), sessionFile.Category!, $"{entry.Id}.md"));
            DeleteFiles(memoryPaths);
            return;
        }

        var memoryFile = ToMemoryFile(entry);
        _memoryFileStore.SaveMemory(agenticOsPath, memoryFile);
        DeleteUnexpectedFiles(
            memoryPaths,
            Path.Combine(FileProtocolPaths.GetMemoryCategoryDir(agenticOsPath, memoryFile.Category!), $"{entry.Id}.md"));
        DeleteFiles(sessionPaths);
    }

    private void DeleteEntryFileFromProtocol(string id)
    {
        var agenticOsPath = ResolveAgenticOsPathForMemory(_storePath);
        if (string.IsNullOrWhiteSpace(agenticOsPath))
            return;

        DeleteFiles(GetMemoryFilePaths(agenticOsPath, id));
        DeleteFiles(GetSessionFilePaths(agenticOsPath, id));
    }

    private static bool ShouldPersistToFileProtocol(MemoryEntry entry)
        => entry.Freshness != FreshnessStatus.Archived;

    private static MemoryFile ToMemoryFile(MemoryEntry entry)
    {
        return new MemoryFile
        {
            Id = entry.Id,
            Type = entry.Type.ToString(),
            Source = entry.Source.ToString(),
            NodeId = entry.NodeId,
            Disciplines = entry.Disciplines.Count > 0 ? [.. entry.Disciplines] : null,
            Tags = entry.Tags.Count > 0 ? [.. entry.Tags] : null,
            Importance = entry.Importance != 0.5 ? entry.Importance : null,
            CreatedAt = entry.CreatedAt,
            LastVerifiedAt = entry.LastVerifiedAt,
            SupersededBy = entry.SupersededBy,
            Body = entry.Content,
            Category = InferMemoryCategory(entry.Type)
        };
    }

    private static SessionFile ToSessionFile(MemoryEntry entry)
    {
        return new SessionFile
        {
            Id = entry.Id,
            Type = entry.Type.ToString(),
            Source = entry.Source.ToString(),
            NodeId = entry.NodeId,
            Tags = entry.Tags.Count > 0 ? [.. entry.Tags] : null,
            CreatedAt = entry.CreatedAt,
            Body = entry.Content,
            Category = InferSessionCategory(entry)
        };
    }

    private static string InferMemoryCategory(MemoryType type)
    {
        return type switch
        {
            MemoryType.Semantic or MemoryType.Structural => FileProtocolPaths.DecisionsDir,
            MemoryType.Episodic => FileProtocolPaths.LessonsDir,
            MemoryType.Procedural => FileProtocolPaths.ConventionsDir,
            _ => FileProtocolPaths.SummariesDir
        };
    }

    private static string InferSessionCategory(MemoryEntry entry)
    {
        if (entry.Type == MemoryType.Working ||
            entry.Tags.Contains(WellKnownTags.ActiveTask, StringComparer.OrdinalIgnoreCase))
        {
            return FileProtocolPaths.TasksDir;
        }

        return FileProtocolPaths.ContextDir;
    }

    private static List<string> GetMemoryFilePaths(string agenticOsPath, string id)
    {
        var fileName = $"{id}.md";
        return
        [
            Path.Combine(FileProtocolPaths.GetMemoryCategoryDir(agenticOsPath, FileProtocolPaths.DecisionsDir), fileName),
            Path.Combine(FileProtocolPaths.GetMemoryCategoryDir(agenticOsPath, FileProtocolPaths.LessonsDir), fileName),
            Path.Combine(FileProtocolPaths.GetMemoryCategoryDir(agenticOsPath, FileProtocolPaths.ConventionsDir), fileName),
            Path.Combine(FileProtocolPaths.GetMemoryCategoryDir(agenticOsPath, FileProtocolPaths.SummariesDir), fileName)
        ];
    }

    private static List<string> GetSessionFilePaths(string agenticOsPath, string id)
    {
        var fileName = $"{id}.md";
        return
        [
            Path.Combine(FileProtocolPaths.GetSessionRoot(agenticOsPath), FileProtocolPaths.TasksDir, fileName),
            Path.Combine(FileProtocolPaths.GetSessionRoot(agenticOsPath), FileProtocolPaths.ContextDir, fileName)
        ];
    }

    private static void DeleteUnexpectedFiles(IEnumerable<string> paths, string expectedPath)
    {
        foreach (var path in paths)
        {
            if (!string.Equals(path, expectedPath, StringComparison.OrdinalIgnoreCase) && File.Exists(path))
                File.Delete(path);
        }
    }

    private static void DeleteFiles(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
