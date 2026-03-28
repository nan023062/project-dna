using Dna.Auth;
using Dna.Core.Config;
using Dna.Core.Framework;
using Dna.Interfaces.Api;
using Dna.Interfaces.Cli;
using Dna.Adapters.Game;
using Dna.Knowledge;
using Dna.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;

// ── 解析存储路径 ──
var dataPath = ResolveDataPath(args);
Environment.SetEnvironmentVariable("DNA_STORE_PATH", dataPath);

// ── 创建应用 ──
DnaApp.Create(args, new AppOptions
{
    AppName = "Project DNA",
    AppDescription = "项目认知引擎",
    DefaultPort = 5051,
    LockScopeProvider = _ => dataPath,
    LogDirectoryProvider = _ => dataPath,
    BannerExtras = (_, port) =>
    {
        var host = GetLocalIp();
        return new List<(string, string)>
        {
            ("REST API:    ", $"http://{host}:{port}/api/"),
            ("MCP Server:  ", $"http://{host}:{port}/mcp"),
            ("Dashboard:   ", $"http://{host}:{port}"),
            ("知识存储:    ", dataPath)
        };
    },
    OnStarted = async sp =>
    {
        var graph = sp.GetRequiredService<IGraphEngine>();
        var memory = sp.GetRequiredService<IMemoryEngine>();
        graph.Initialize(dataPath);
        memory.Initialize(dataPath);
        try { graph.BuildTopology(); } catch { /* empty store is fine */ }
        await Task.CompletedTask;
    }
});

// ── CLI 命令 ──
DnaApp.AddCliCommand(new DefaultCliCommand());

// ── 注册服务 ──
// TODO: 逐步将这些服务改造为 IDnaService 后，改用 DnaApp.Register<T>()
DnaApp.ConfigureServices(services =>
{
    services.AddSingleton<ProjectConfig>();
    services.AddKnowledgeGraph();
    if (DnaApp.Mode != AppRunMode.Stdio)
        services.AddHostedService<KnowledgeCondenseScheduler>();

    var jwtService = new JwtService();
    services.AddSingleton(jwtService);
    services.AddSingleton<UserStore>(sp =>
    {
        var store = new UserStore();
        store.Initialize(dataPath);
        return store;
    });

    services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(opts => opts.TokenValidationParameters = jwtService.GetValidationParameters());
    services.AddAuthorization();

    var personaName = ResolvePersonaName(dataPath);
    if (DnaApp.Mode == AppRunMode.Stdio)
    {
        services.AddMcpServer(opts =>
        {
            opts.ServerInfo = new() { Name = personaName, Version = "1.0.0" };
        }).WithStdioServerTransport().WithToolsFromAssembly();
    }
    else
    {
        services.AddMcpServer(opts =>
        {
            opts.ServerInfo = new() { Name = personaName, Version = "1.0.0" };
        }).WithHttpTransport().WithToolsFromAssembly();
    }
});

// ── Web 管道 ──
DnaApp.ConfigureWebApp(web =>
{
    web.UseMiddleware<RequestLoggingMiddleware>();
    web.UseAuthentication();
    web.UseAuthorization();
    web.MapAuthEndpoints();
    web.MapApiEndpoints(DateTime.UtcNow);
    web.MapMcp("/mcp");
});

return await DnaApp.RunAsync();

static string ResolveDataPath(string[] args)
{
    for (var i = 0; i < args.Length; i++)
    {
        if (!string.Equals(args[i], "--db", StringComparison.OrdinalIgnoreCase)) continue;

        // --db <路径> 或 --db（无参数，用当前目录）
        if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
            return Path.GetFullPath(args[i + 1]);

        return Directory.GetCurrentDirectory();
    }

    var envStore = Environment.GetEnvironmentVariable("DNA_STORE_PATH");
    if (!string.IsNullOrEmpty(envStore))
        return Path.GetFullPath(envStore);

    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine("错误：必须指定知识库路径。");
    Console.ResetColor();
    Console.WriteLine();
    Console.WriteLine("用法：");
    Console.WriteLine("  dna --db                          # 用当前目录作为知识库");
    Console.WriteLine("  dna --db <知识库目录>");
    Console.WriteLine("  dna --db --port 5051              # 当前目录 + 指定端口");
    Console.WriteLine("  dna --db ~/.dna/my-game --port 5051");
    Console.WriteLine();
    Console.WriteLine("或设置环境变量 DNA_STORE_PATH。");
    Environment.Exit(1);
    return "";
}

static string ResolvePersonaName(string storePath)
{
    // graph/memory are DB-first in the new architecture; persona name falls back to default.
    _ = storePath;
    return "Project DNA";
}

static string GetLocalIp()
{
    try
    {
        using var socket = new System.Net.Sockets.Socket(
            System.Net.Sockets.AddressFamily.InterNetwork,
            System.Net.Sockets.SocketType.Dgram, 0);
        socket.Connect("8.8.8.8", 80);
        if (socket.LocalEndPoint is System.Net.IPEndPoint ep)
            return ep.Address.ToString();
    }
    catch { /* fallback */ }
    return "localhost";
}
