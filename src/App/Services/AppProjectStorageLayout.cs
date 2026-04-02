using Dna.Core.Config;
using Microsoft.Extensions.Logging;

namespace Dna.App.Services;

internal sealed record AppProjectStorageLayout(
    string MetadataRootPath,
    string MemoryRootPath,
    string KnowledgeRootPath,
    int MigratedMemoryFileCount,
    int MigratedKnowledgeFileCount);

internal static class AppProjectStoragePaths
{
    private static readonly string[] SqliteSidecars = ["", "-wal", "-shm"];
    private static readonly string[] KnowledgeFiles =
    [
        "architecture.json",
        "modules.json",
        "modules.computed.json",
        "modules.manifest.json"
    ];

    public static AppProjectStorageLayout Prepare(string metadataRootPath, ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(metadataRootPath);

        var normalizedMetadataRoot = Path.GetFullPath(metadataRootPath);
        var memoryRoot = ProjectConfig.ResolveMemoryStorePath(normalizedMetadataRoot);
        var knowledgeRoot = ProjectConfig.ResolveKnowledgeStorePath(normalizedMetadataRoot);

        Directory.CreateDirectory(normalizedMetadataRoot);
        Directory.CreateDirectory(memoryRoot);
        Directory.CreateDirectory(knowledgeRoot);

        var migratedMemoryFileCount = 0;
        var migratedKnowledgeFileCount = 0;

        MoveSqliteWithSidecars(
            Path.Combine(normalizedMetadataRoot, "memory.db"),
            Path.Combine(memoryRoot, "memory.db"),
            logger,
            ref migratedMemoryFileCount);

        MoveSqliteWithSidecars(
            Path.Combine(normalizedMetadataRoot, "index.db"),
            Path.Combine(memoryRoot, "index.db"),
            logger,
            ref migratedMemoryFileCount);

        MoveSqliteWithSidecars(
            Path.Combine(normalizedMetadataRoot, "graph.db"),
            Path.Combine(knowledgeRoot, "graph.db"),
            logger,
            ref migratedKnowledgeFileCount);

        foreach (var fileName in KnowledgeFiles)
        {
            MoveFileIfNeeded(
                Path.Combine(normalizedMetadataRoot, fileName),
                Path.Combine(knowledgeRoot, fileName),
                logger,
                ref migratedKnowledgeFileCount);
        }

        return new AppProjectStorageLayout(
            normalizedMetadataRoot,
            memoryRoot,
            knowledgeRoot,
            migratedMemoryFileCount,
            migratedKnowledgeFileCount);
    }

    private static void MoveSqliteWithSidecars(
        string sourceDbPath,
        string targetDbPath,
        ILogger logger,
        ref int migratedFileCount)
    {
        foreach (var suffix in SqliteSidecars)
        {
            MoveFileIfNeeded(
                sourceDbPath + suffix,
                targetDbPath + suffix,
                logger,
                ref migratedFileCount);
        }
    }

    private static void MoveFileIfNeeded(
        string sourcePath,
        string targetPath,
        ILogger logger,
        ref int migratedFileCount)
    {
        if (!File.Exists(sourcePath))
            return;

        if (string.Equals(
                Path.GetFullPath(sourcePath),
                Path.GetFullPath(targetPath),
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (File.Exists(targetPath))
        {
            logger.LogWarning(
                "检测到旧数据与新数据同时存在，跳过自动迁移: source={Source} target={Target}",
                sourcePath,
                targetPath);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        File.Move(sourcePath, targetPath);
        migratedFileCount++;

        logger.LogInformation(
            "已迁移旧的 App 本地数据: {Source} -> {Target}",
            sourcePath,
            targetPath);
    }
}
