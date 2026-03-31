using Dna.Client.Interfaces.Api;
using Dna.Client.Interfaces.Cli;
using Dna.Client.Services;
using Dna.Client.Services.Tooling;
using Dna.Core.Framework;
using Dna.Web.Shared.AgentShell;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;

var serverBaseUrl = ClientBootstrap.ResolveServerBaseUrl(args);
var workspaceRoot = ClientBootstrap.ResolveWorkspaceRoot(args);
var workspaceConfigPath = ClientBootstrap.ResolveWorkspaceConfigPath(args);
var defaultClientPort = ClientBootstrap.ResolveClientDefaultPort(args);
var appArgs = ClientBootstrap.SanitizeArgsForFixedPort(args);

var app = DnaApp.Create(appArgs, new AppOptions
{
    AppName = "Project DNA Client",
    AppDescription = "Project DNA Client（本地 MCP + 独立 Agent 宿主）",
    DefaultPort = defaultClientPort,
    AllowPortAutoFallback = false,
    LockScopeProvider = _ => "project-dna-client",
    BannerExtras = (_, port) =>
    {
        var host = ClientBootstrap.GetLocalIp();
        return
        [
            ("Client API:  ", $"http://{host}:{port}/api/client/status"),
            ("MCP Server:  ", $"http://{host}:{port}/mcp"),
            ("DNA Server:  ", serverBaseUrl)
        ];
    }
});

app.AddCliCommand(new DefaultCliCommand());

app.ConfigureServices(services =>
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

    if (app.Mode == AppRunMode.Stdio)
    {
        services.AddMcpServer(opts =>
        {
            opts.ServerInfo = new() { Name = "project-dna-client", Version = "1.0.0" };
        }).WithStdioServerTransport().WithToolsFromAssembly();
    }
    else
    {
        services.AddMcpServer(opts =>
        {
            opts.ServerInfo = new() { Name = "project-dna-client", Version = "1.0.0" };
        }).WithHttpTransport().WithToolsFromAssembly();
    }
});

app.ConfigureWebApp(web =>
{
    web.MapClientStatusEndpoints();
    web.MapClientWorkspaceEndpoints();
    web.MapClientDiscoveryEndpoints();
    web.MapClientProxyEndpoints();
    web.MapClientAgentProxyEndpoints();
    web.MapClientToolingEndpoints();
    web.MapMcp("/mcp");
});

return await app.RunAsync();
