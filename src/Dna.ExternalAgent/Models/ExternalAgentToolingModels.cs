namespace Dna.ExternalAgent.Models;

public sealed class ExternalAgentManagedFileStatus
{
    public string RelativePath { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public string Purpose { get; init; } = string.Empty;
    public bool Exists { get; init; }
}

public sealed class ExternalAgentToolingTargetStatus
{
    public string ProductId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string InstallMode { get; init; } = string.Empty;
    public bool Installed { get; init; }
    public bool McpConfigured { get; init; }
    public IReadOnlyList<ExternalAgentManagedFileStatus> ManagedFiles { get; init; } = [];
}

public sealed class ExternalAgentToolingInstallReport
{
    public string ProductId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string InstallMode { get; init; } = string.Empty;
    public IReadOnlyList<ExternalAgentManagedFileStatus> ManagedFiles { get; init; } = [];
    public List<string> WrittenFiles { get; init; } = [];
    public List<string> SkippedFiles { get; init; } = [];
    public List<string> BackupFiles { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
}
