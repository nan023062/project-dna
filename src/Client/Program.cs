using Dna.Client.Interfaces.Api;
using Dna.Client.Interfaces.Cli;
using Dna.Client.Services;
using Dna.Client.Services.Pipeline;
using Dna.Client.Services.Tooling;
using Dna.Core.Framework;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;

var serverBaseUrl = ClientBootstrap.ResolveServerBaseUrl(args);

DnaApp.Create(args, new AppOptions
{
    AppName = "Project DNA Client",
    AppDescription = "决策与执行客户端（MCP + Agent 入口）",
    DefaultPort = 5052,
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

DnaApp.AddCliCommand(new DefaultCliCommand());

DnaApp.ConfigureServices(services =>
{
    services.AddSingleton(new ClientRuntimeOptions { ServerBaseUrl = serverBaseUrl });
    services.AddHttpContextAccessor();
    services.AddTransient<ForwardAuthHeaderHandler>();
    services.AddHttpClient<DnaServerApi>((_, client) =>
    {
        client.BaseAddress = new Uri(serverBaseUrl.TrimEnd('/'));
        client.Timeout = TimeSpan.FromSeconds(30);
    }).AddHttpMessageHandler<ForwardAuthHeaderHandler>();
    services.AddSingleton<ClientPipelineStore>();
    services.AddSingleton<AgentPipelineRunner>();
    services.AddSingleton<ClientIdeToolingService>();

    if (DnaApp.Mode == AppRunMode.Stdio)
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

DnaApp.ConfigureWebApp(web =>
{
    web.MapClientStatusEndpoints();
    web.MapClientProxyEndpoints();
    web.MapClientPipelineEndpoints();
    web.MapClientToolingEndpoints();
    web.MapMcp("/mcp");
});

return await DnaApp.RunAsync();
