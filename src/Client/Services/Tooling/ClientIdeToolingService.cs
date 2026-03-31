namespace Dna.Client.Services.Tooling;

public sealed class ClientIdeToolingService(
    ClientToolingTargetCatalog targetCatalog,
    ClientToolingContentBuilder contentBuilder,
    ClientToolingFileManager fileManager)
{
    public ClientToolingTargetStatus GetStatus(
        string target,
        string workspaceRoot,
        string mcpEndpoint,
        string serverName)
    {
        var definition = targetCatalog.Get(target, workspaceRoot);
        var mcpConfigured = fileManager.IsMcpConfigured(definition.Paths.McpFile, mcpEndpoint, serverName);
        var filesExist = new[]
        {
            definition.Paths.McpFile,
            definition.Paths.PromptFile,
            definition.Paths.AgentFile
        }.All(File.Exists);

        return new ClientToolingTargetStatus
        {
            Id = definition.Id,
            DisplayName = definition.DisplayName,
            Description = definition.Description,
            Installed = filesExist && mcpConfigured,
            McpConfigured = mcpConfigured,
            Paths = definition.Paths
        };
    }

    public ClientToolingInstallReport InstallTarget(
        string target,
        string workspaceRoot,
        string mcpEndpoint,
        string serverName,
        bool replaceExisting)
    {
        var definition = targetCatalog.Get(target, workspaceRoot);
        var report = new ClientToolingInstallReport
        {
            Target = definition.Id,
            DisplayName = definition.DisplayName,
            Paths = definition.Paths
        };

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(definition.Paths.McpFile)!);
            Directory.CreateDirectory(Path.GetDirectoryName(definition.Paths.PromptFile)!);
            Directory.CreateDirectory(Path.GetDirectoryName(definition.Paths.AgentFile)!);

            fileManager.UpdateMcpConfig(definition.Paths.McpFile, serverName, mcpEndpoint, report);
            fileManager.WriteManagedFile(
                definition.Paths.PromptFile,
                contentBuilder.BuildPromptContent(definition.Id, mcpEndpoint),
                replaceExisting,
                report);
            fileManager.WriteManagedFile(
                definition.Paths.AgentFile,
                contentBuilder.BuildAgentContent(mcpEndpoint, serverName, definition.Id),
                replaceExisting,
                report);
        }
        catch (Exception ex)
        {
            report.Warnings.Add(ex.Message);
        }

        return report;
    }
}

public sealed class ClientToolingTargetPaths
{
    public string McpFile { get; init; } = "";
    public string PromptFile { get; init; } = "";
    public string AgentFile { get; init; } = "";
}

public sealed class ClientToolingTargetStatus
{
    public string Id { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Description { get; init; } = "";
    public bool Installed { get; init; }
    public bool McpConfigured { get; init; }
    public ClientToolingTargetPaths Paths { get; init; } = new();
}

public sealed class ClientToolingInstallReport
{
    public string Target { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public ClientToolingTargetPaths Paths { get; init; } = new();
    public List<string> WrittenFiles { get; init; } = [];
    public List<string> SkippedFiles { get; init; } = [];
    public List<string> BackupFiles { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
}
