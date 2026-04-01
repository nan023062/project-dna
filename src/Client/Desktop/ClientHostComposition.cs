using Dna.Client.Interfaces.Api;
using Dna.Client.Services;
using Dna.Client.Services.Tooling;
using Dna.Web.Shared.AgentShell;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;

namespace Dna.Client.Desktop;

internal static class ClientHostComposition
{
    public static void ConfigureServices(
        IServiceCollection services,
        string serverBaseUrl,
        string workspaceRoot,
        string metadataRootPath,
        string? workspaceConfigPath,
        string? agentShellRootPath)
    {
        services.AddSingleton(new ClientRuntimeOptions
        {
            ServerBaseUrl = serverBaseUrl,
            WorkspaceRoot = workspaceRoot,
            MetadataRootPath = metadataRootPath,
            WorkspaceConfigPath = workspaceConfigPath,
            AgentShellRootPath = agentShellRootPath
        });

        services.AddSingleton<ClientWorkspaceStore>();
        services.AddSingleton<ClientProjectLlmConfigService>();
        services.AddSingleton<IAgentShellContext, ClientAgentShellContext>();
        services.AddSingleton(sp => new AgentShellStorageOptions
        {
            RootDirectory = string.IsNullOrWhiteSpace(agentShellRootPath)
                ? Path.Combine(metadataRootPath, "agent-shell")
                : Path.GetFullPath(agentShellRootPath)
        });
        services.AddSingleton<AgentShellService>();

        services.AddHttpClient<DnaServerApi>((_, client) =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddHttpClient<ServerDiscoveryService>((_, client) =>
        {
            client.Timeout = TimeSpan.FromMilliseconds(400);
        });

        services.AddClientToolingServices();
        services.AddSingleton<ClientFolderPickerService>();

        services.AddMcpServer(opts =>
        {
            opts.ServerInfo = new() { Name = "project-dna-client", Version = "1.0.0" };
        }).WithHttpTransport().WithToolsFromAssembly();
    }

    public static void ConfigureWebApp(WebApplication web)
    {
        web.MapClientStatusEndpoints();
        web.MapClientLlmConfigEndpoints();
        web.MapClientWorkspaceEndpoints();
        web.MapClientDiscoveryEndpoints();
        web.MapClientProxyEndpoints();
        web.MapClientAgentProxyEndpoints();
        web.MapClientToolingEndpoints();
        web.MapMcp("/mcp");
    }
}
