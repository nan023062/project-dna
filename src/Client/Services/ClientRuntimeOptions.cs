namespace Dna.Client.Services;

public sealed class ClientRuntimeOptions
{
    private string _apiBaseUrl = ClientRuntimeConstants.ApiBaseUrl;

    public string ApiBaseUrl
    {
        get => _apiBaseUrl;
        init => _apiBaseUrl = NormalizeBaseUrl(value);
    }

    public string ServerBaseUrl
    {
        get => _apiBaseUrl;
        init => _apiBaseUrl = NormalizeBaseUrl(value);
    }

    public string ProjectName { get; init; } = "Project";
    public string WorkspaceRoot { get; init; } = Directory.GetCurrentDirectory();
    public string? MetadataRootPath { get; init; }
    public string? WorkspaceConfigPath { get; init; }
    public string? AgentShellRootPath { get; init; }
    public DateTime StartedAtUtc { get; init; } = DateTime.UtcNow;

    private static string NormalizeBaseUrl(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return ClientRuntimeConstants.ApiBaseUrl;

        return ClientBootstrap.NormalizeUrl(raw);
    }
}
