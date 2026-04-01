namespace Dna.Client.Services;

public sealed class ClientRuntimeOptions
{
    public string ServerBaseUrl { get; init; } = "http://localhost:5051";
    public string WorkspaceRoot { get; init; } = Directory.GetCurrentDirectory();
    public string? MetadataRootPath { get; init; }
    public string? WorkspaceConfigPath { get; init; }
    public string? AgentShellRootPath { get; init; }
}
