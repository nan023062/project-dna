using Dna.Knowledge.Workspace.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dna.Knowledge.Workspace;

internal static class WorkspaceMetadataFile
{
    public const string FileName = WorkspaceConstants.Metadata.FileName;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string GetMetadataFilePath(string projectRoot, string directoryRelativePath)
    {
        var directoryPath = WorkspacePath.ResolveFullPathWithinRoot(projectRoot, directoryRelativePath);
        return Path.Combine(directoryPath, FileName);
    }

    public static WorkspaceDirectoryMetadataDocument? TryRead(string projectRoot, string directoryRelativePath)
    {
        var filePath = GetMetadataFilePath(projectRoot, directoryRelativePath);
        if (!File.Exists(filePath))
            return null;

        try
        {
            var json = File.ReadAllText(filePath);
            return Normalize(JsonSerializer.Deserialize<WorkspaceDirectoryMetadataDocument>(json, JsonOptions));
        }
        catch
        {
            return null;
        }
    }

    public static async Task<WorkspaceDirectoryMetadataDocument?> TryReadAsync(
        string projectRoot,
        string directoryRelativePath,
        CancellationToken cancellationToken = default)
    {
        var filePath = GetMetadataFilePath(projectRoot, directoryRelativePath);
        if (!File.Exists(filePath))
            return null;

        try
        {
            var json = await File.ReadAllTextAsync(filePath, cancellationToken);
            return Normalize(JsonSerializer.Deserialize<WorkspaceDirectoryMetadataDocument>(json, JsonOptions));
        }
        catch
        {
            return null;
        }
    }

    public static async Task<WorkspaceDirectoryMetadataDocument> WriteAsync(
        string projectRoot,
        string directoryRelativePath,
        WorkspaceDirectoryMetadataDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        var normalized = Normalize(document) ?? new WorkspaceDirectoryMetadataDocument();
        normalized.UpdatedAtUtc = DateTime.UtcNow;

        var directoryPath = WorkspacePath.ResolveFullPathWithinRoot(projectRoot, directoryRelativePath);
        Directory.CreateDirectory(directoryPath);

        var filePath = Path.Combine(directoryPath, FileName);
        var json = JsonSerializer.Serialize(normalized, JsonOptions);
        await File.WriteAllTextAsync(filePath, json, cancellationToken);
        return normalized;
    }

    public static async Task<WorkspaceDirectoryMetadataDocument> EnsureAsync(
        string projectRoot,
        string directoryRelativePath,
        CancellationToken cancellationToken = default)
    {
        var existing = await TryReadAsync(projectRoot, directoryRelativePath, cancellationToken);
        if (existing != null)
            return existing;

        return await WriteAsync(
            projectRoot,
            directoryRelativePath,
            new WorkspaceDirectoryMetadataDocument(),
            cancellationToken);
    }

    public static WorkspaceDirectoryDescriptorInfo? ToDescriptorInfo(
        WorkspaceDirectoryMetadataDocument? document,
        string directoryRelativePath)
    {
        if (document == null)
            return null;

        var normalizedPath = WorkspacePath.NormalizeRelativePath(directoryRelativePath);
        var fileRelativePath = normalizedPath.Length == 0
            ? FileName
            : $"{normalizedPath}{WorkspaceConstants.Paths.RelativeSeparator}{FileName}";

        return new WorkspaceDirectoryDescriptorInfo
        {
            FileName = FileName,
            RelativeFilePath = fileRelativePath,
            StableGuid = document.StableGuid,
            Summary = document.Summary
        };
    }

    private static WorkspaceDirectoryMetadataDocument? Normalize(WorkspaceDirectoryMetadataDocument? document)
    {
        if (document == null)
            return null;

        document.Schema = string.IsNullOrWhiteSpace(document.Schema)
            ? WorkspaceConstants.Metadata.Schema
            : document.Schema.Trim();
        document.StableGuid = string.IsNullOrWhiteSpace(document.StableGuid)
            ? Guid.NewGuid().ToString("N")
            : document.StableGuid.Trim();
        document.Summary = NormalizeOptional(document.Summary);

        return document;
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
