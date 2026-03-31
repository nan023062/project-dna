namespace Dna.Client.Services;

public sealed class ClientRuntimeOptions
{
    public string ServerBaseUrl { get; init; } = "http://localhost:5051";
    public string WorkspaceRoot { get; init; } = Directory.GetCurrentDirectory();
    public string? WorkspaceConfigPath { get; init; }
}
