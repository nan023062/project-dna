using Dna.Auth;
using Dna.Core.Config;
using Dna.Core.Framework;
using Dna.Interfaces.Api;
using Dna.Interfaces.Cli;
using Dna.Adapters.Game;
using Dna.Knowledge;
using Dna.Memory.Models;
using Dna.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Logging;

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
        await EnsureDevGroupFirstStartupAndRetrospectiveAsync(sp, graph, memory);
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
    _ = personaName;
});

// ── Web 管道 ──
DnaApp.ConfigureWebApp(web =>
{
    web.UseMiddleware<RequestLoggingMiddleware>();
    web.UseAuthentication();
    web.UseAuthorization();
    web.MapAuthEndpoints();
    web.MapApiEndpoints(DateTime.UtcNow);
});

return await DnaApp.RunAsync();

static Task EnsureDevGroupFirstStartupAndRetrospectiveAsync(
    IServiceProvider sp,
    IGraphEngine graph,
    IMemoryEngine memory)
{
    const string devGroupName = "开发组";
    var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("StartupWorkflow");

    try
    {
        var devGroup = graph.FindModule(devGroupName);
        if (devGroup == null)
        {
            logger.LogWarning("启动流程未找到模块 '{DevGroup}'，跳过优先启动与复盘。", devGroupName);
            return Task.CompletedTask;
        }

        // 启动时优先激活开发组上下文，作为首个工作模块。
        var context = graph.GetModuleContext(devGroupName, devGroupName, [devGroupName]);
        logger.LogInformation(
            "启动流程：已优先激活模块 [{Module}]，约束={ConstraintCount}。",
            devGroupName,
            context.Constraints?.Count ?? 0);

        var lastCompleted = memory.QueryMemories(new MemoryFilter
        {
            NodeId = devGroupName,
            Tags = ["#completed-task"],
            Freshness = FreshnessFilter.All,
            Limit = 1
        }).FirstOrDefault();

        var lastLesson = memory.QueryMemories(new MemoryFilter
        {
            NodeId = devGroupName,
            Tags = ["#lesson"],
            Freshness = FreshnessFilter.All,
            Limit = 1
        }).FirstOrDefault();

        if (lastCompleted == null && lastLesson == null)
        {
            logger.LogInformation("启动复盘：模块 [{Module}] 暂无可复盘的历史任务记录。", devGroupName);
            return Task.CompletedTask;
        }

        if (lastCompleted != null)
        {
            logger.LogInformation(
                "启动复盘-上次完成：[{Id}] {Summary} @ {CreatedAt}",
                lastCompleted.Id,
                lastCompleted.Summary ?? "(无摘要)",
                lastCompleted.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
        }

        if (lastLesson != null)
        {
            logger.LogInformation(
                "启动复盘-上次教训：[{Id}] {Summary} @ {CreatedAt}",
                lastLesson.Id,
                lastLesson.Summary ?? "(无摘要)",
                lastLesson.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
        }
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "启动流程执行开发组优先启动/上次任务复盘时发生异常，已降级继续启动。");
    }

    return Task.CompletedTask;
}

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
