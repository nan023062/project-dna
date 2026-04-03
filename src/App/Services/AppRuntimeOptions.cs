namespace Dna.App.Services;

public sealed class AppRuntimeOptions
{
    private string _apiBaseUrl = AppRuntimeConstants.ApiBaseUrl;

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
    public DateTime StartedAtUtc { get; init; } = DateTime.UtcNow;

    private static string NormalizeBaseUrl(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return AppRuntimeConstants.ApiBaseUrl;

        return AppBootstrap.NormalizeUrl(raw);
    }
}
