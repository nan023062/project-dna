using Dna.Client.Interfaces.Api;
using Dna.Client.Interfaces.Cli;
using Dna.Client.Services;
using Dna.Client.Services.Tooling;
using Dna.Core.Framework;
using Dna.Web.Shared.AgentShell;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;

namespace Dna.Client.Desktop;

internal static class ClientHostComposition
{
    public static void ConfigureDnaApp(
        DnaApp app,
        string serverBaseUrl,
        string workspaceRoot,
        string? workspaceConfigPath)
    {
        app.AddCliCommand(new DefaultCliCommand());
        app.ConfigureServices(services => ConfigureServices(services, app.Mode, serverBaseUrl, workspaceRoot, workspaceConfigPath));

        if (app.Mode != AppRunMode.Stdio)
            app.ConfigureWebApp(ConfigureWebApp);
    }

    public static void ConfigureServices(
        IServiceCollection services,
        AppRunMode mode,
        string serverBaseUrl,
        string workspaceRoot,
        string? workspaceConfigPath)
    {
        services.AddSingleton(new ClientRuntimeOptions
        {
            ServerBaseUrl = serverBaseUrl,
            WorkspaceRoot = workspaceRoot,
            WorkspaceConfigPath = workspaceConfigPath
        });

        services.AddSingleton<ClientWorkspaceStore>();
        services.AddSingleton<IAgentShellContext, ClientAgentShellContext>();
        services.AddSingleton(sp => new AgentShellStorageOptions
        {
            RootDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".dna",
                "client-agent-shell")
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

        if (mode == AppRunMode.Stdio)
        {
            services.AddMcpServer(opts =>
            {
                opts.ServerInfo = new() { Name = "project-dna-client", Version = "1.0.0" };
            }).WithStdioServerTransport().WithToolsFromAssembly();
            return;
        }

        services.AddMcpServer(opts =>
        {
            opts.ServerInfo = new() { Name = "project-dna-client", Version = "1.0.0" };
        }).WithHttpTransport().WithToolsFromAssembly();
    }

    public static void ConfigureWebApp(WebApplication web)
    {
        web.MapClientStatusEndpoints();
        web.MapClientWorkspaceEndpoints();
        web.MapClientDiscoveryEndpoints();
        web.MapClientProxyEndpoints();
        web.MapClientAgentProxyEndpoints();
        web.MapClientToolingEndpoints();
        web.MapMcp("/mcp");
    }
}
