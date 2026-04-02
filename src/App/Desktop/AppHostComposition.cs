using Dna.App.Interfaces.Api;
using Dna.App.Services;
using Dna.App.Services.AgentShell;
using Dna.App.Services.Tooling;
using Dna.Core.Config;
using Dna.Knowledge;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;

namespace Dna.App.Desktop;

internal static class AppHostComposition
{
    public static void ConfigureServices(
        IServiceCollection services,
        string projectName,
        string workspaceRoot,
        string metadataRootPath,
        string? workspaceConfigPath,
        string? agentShellRootPath)
    {
        services.AddSingleton(new AppRuntimeOptions
        {
            ApiBaseUrl = AppRuntimeConstants.ApiBaseUrl,
            ProjectName = projectName,
            WorkspaceRoot = workspaceRoot,
            MetadataRootPath = metadataRootPath,
            WorkspaceConfigPath = workspaceConfigPath,
            AgentShellRootPath = agentShellRootPath
        });

        services.AddSingleton<ProjectConfig>();
        services.AddKnowledgeGraph();
        services.AddHostedService<AppLocalRuntimeInitializer>();
        services.AddSingleton<AppWorkspaceStore>();
        services.AddSingleton<AppProjectLlmConfigService>();
        services.AddSingleton<IAgentShellContext, AppAgentShellContext>();
        services.AddSingleton(sp => new AgentShellStorageOptions
        {
            RootDirectory = string.IsNullOrWhiteSpace(agentShellRootPath)
                ? Path.Combine(metadataRootPath, "agent-shell")
                : Path.GetFullPath(agentShellRootPath)
        });
        services.AddSingleton<AgentShellService>();

        services.AddHttpClient<DnaServerApi>((_, app) =>
        {
            app.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddAppToolingServices();
        services.AddSingleton<AppFolderPickerService>();

        services.AddMcpServer(opts =>
        {
            opts.ServerInfo = new() { Name = "agentic-os-app", Version = "1.0.0" };
        }).WithHttpTransport().WithToolsFromAssembly();
    }

    public static void ConfigureWebApp(WebApplication web)
    {
        web.MapAppLocalKnowledgeEndpoints();
        web.MapAppStatusEndpoints();
        web.MapAppLlmConfigEndpoints();
        web.MapAppWorkspaceEndpoints();
        web.MapAppDiscoveryEndpoints();
        web.MapAppAgentProxyEndpoints();
        web.MapAppToolingEndpoints();
        web.MapMcp("/mcp");
    }
}
