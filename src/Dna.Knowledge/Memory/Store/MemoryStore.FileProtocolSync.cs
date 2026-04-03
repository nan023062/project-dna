using Dna.Knowledge.FileProtocol;
using Dna.Knowledge.FileProtocol.Models;
using Dna.Memory.Models;

namespace Dna.Memory.Store;

public partial class MemoryStore
{
    private readonly MemoryFileStore _memoryFileStore = new();

    private void SyncEntryToFileProtocol(MemoryEntry entry)
    {
        var agenticOsPath = ResolveAgenticOsPathForMemory(_storePath);
        if (string.IsNullOrWhiteSpace(agenticOsPath))
            return;

        var existingPaths = GetMemoryFilePaths(agenticOsPath, entry.Id);

        if (!ShouldPersistToFileProtocol(entry))
        {
            DeleteMemoryFiles(existingPaths);
            return;
        }

        var memoryFile = ToMemoryFile(entry);
        _memoryFileStore.SaveMemory(agenticOsPath, memoryFile);

        var expectedPath = Path.Combine(
            FileProtocolPaths.GetMemoryCategoryDir(agenticOsPath, memoryFile.Category!),
            $"{entry.Id}.md");

        foreach (var path in existingPaths)
        {
            if (!string.Equals(path, expectedPath, StringComparison.OrdinalIgnoreCase) && File.Exists(path))
                File.Delete(path);
        }
    }

    private void DeleteEntryFileFromProtocol(string id)
    {
        var agenticOsPath = ResolveAgenticOsPathForMemory(_storePath);
        if (string.IsNullOrWhiteSpace(agenticOsPath))
            return;

        DeleteMemoryFiles(GetMemoryFilePaths(agenticOsPath, id));
    }

    private static bool ShouldPersistToFileProtocol(MemoryEntry entry)
        => entry.Stage != MemoryStage.ShortTerm && entry.Freshness != FreshnessStatus.Archived;

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

    private static void DeleteMemoryFiles(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
