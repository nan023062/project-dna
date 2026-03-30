using Dna.Client.Interfaces.Cli;
using Dna.Client.Services;
using Dna.Client.Services.Pipeline;
using Dna.Core.Framework;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;

var serverBaseUrl = ResolveServerBaseUrl(args);

DnaApp.Create(args, new AppOptions
{
    AppName = "Project DNA Client",
    AppDescription = "决策与执行客户端（MCP + Agent 入口）",
    DefaultPort = 5052,
    BannerExtras = (_, port) =>
    {
        var host = GetLocalIp();
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
    services.AddHttpClient<DnaServerApi>((_, client) =>
    {
        client.BaseAddress = new Uri(serverBaseUrl.TrimEnd('/'));
        client.Timeout = TimeSpan.FromSeconds(30);
    });
    services.AddSingleton<ClientPipelineStore>();
    services.AddSingleton<AgentPipelineRunner>();

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
    web.MapGet("/", () => Results.Text("Project DNA Client is running."));

    web.MapGet("/api/client/status", async (DnaServerApi api) =>
    {
        try
        {
            var serverStatus = await api.GetAsync("/api/status");
            return Results.Ok(new
            {
                client = "ok",
                targetServer = api.BaseUrl,
                serverStatus
            });
        }
        catch (Exception ex)
        {
            return Results.Ok(new
            {
                client = "degraded",
                targetServer = api.BaseUrl,
                error = ex.Message
            });
        }
    });

    web.MapGet("/api/client/pipeline/config", (ClientPipelineStore store) =>
    {
        var config = store.GetConfig();
        return Results.Ok(config);
    });

    web.MapPut("/api/client/pipeline/config", (
        PipelineUpdateRequest request,
        ClientPipelineStore store) =>
    {
        if (request.Config == null)
            return Results.BadRequest(new { error = "config 不能为空" });

        var updated = store.UpdateConfig(request.Config);
        return Results.Ok(updated);
    });

    web.MapGet("/api/client/pipeline/runs/latest", (ClientPipelineStore store) =>
    {
        var latest = store.GetLatestRun();
        return latest == null
            ? Results.NotFound(new { error = "暂无执行记录" })
            : Results.Ok(latest);
    });

    web.MapPost("/api/client/pipeline/run", async (
        PipelineRunRequest request,
        AgentPipelineRunner runner) =>
    {
        var result = await runner.RunAsync(request);
        return Results.Ok(result);
    });

    web.MapMcp("/mcp");
});

return await DnaApp.RunAsync();

static string ResolveServerBaseUrl(string[] args)
{
    for (var i = 0; i < args.Length; i++)
    {
        if (!string.Equals(args[i], "--server", StringComparison.OrdinalIgnoreCase)) continue;
        if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
            return NormalizeUrl(args[i + 1]);
    }

    var env = Environment.GetEnvironmentVariable("DNA_SERVER_URL")
              ?? Environment.GetEnvironmentVariable("DNA_URL");
    if (!string.IsNullOrWhiteSpace(env))
        return NormalizeUrl(env);

    return "http://localhost:5051";
}

static string NormalizeUrl(string raw) => raw.Trim().TrimEnd('/');

static string GetLocalIp()
{
    try
    {
        using var socket = new System.Net.Sockets.Socket(
            System.Net.Sockets.AddressFamily.InterNetwork,
            System.Net.Sockets.SocketType.Dgram, 0);
        socket.Connect("8.8.8.8", 80);
        if (socket.LocalEndPoint is System.Net.IPEndPoint endpoint)
            return endpoint.Address.ToString();
    }
    catch
    {
    }

    return "localhost";
}
