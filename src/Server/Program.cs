using Dna.Adapters.Game;
using Dna.Auth;
using Dna.Core.Config;
using Dna.Core.Framework;
using Dna.Interfaces.Api;
using Dna.Interfaces.Cli;
using Dna.Knowledge;
using Dna.Review;
using Dna.Services;
using Dna.Web.Shared.AgentShell;
using Microsoft.AspNetCore.Authentication.JwtBearer;

var runtimeOptions = ServerBootstrap.CreateRuntimeOptions(args);

var app = DnaApp.Create(args, new AppOptions
{
    AppName = "Project DNA",
    AppDescription = "项目认知引擎",
    DefaultPort = 5051,
    LockScopeProvider = _ => runtimeOptions.DataPath,
    LogDirectoryProvider = _ => runtimeOptions.DataPath,
    BannerExtras = (_, port) =>
    {
        var host = ServerBootstrap.GetLocalIp();
        return new List<(string, string)>
        {
            ("REST API:    ", $"http://{host}:{port}/api/"),
            ("Dashboard:   ", $"http://{host}:{port}"),
            ("知识存储:    ", runtimeOptions.DataPath)
        };
    },
    OnStarted = sp => sp.GetRequiredService<ServerStartupWorkflow>().RunAsync()
});

app.AddCliCommand(new DefaultCliCommand());

app.ConfigureServices(services =>
{
    services.AddSingleton(runtimeOptions);
    services.AddSingleton<ServerStartupWorkflow>();
    services.AddSingleton<ProjectConfig>();
    services.AddKnowledgeGraph();
    services.AddSingleton<IAgentShellContext, ServerAgentShellContext>();
    services.AddSingleton(sp => new AgentShellStorageOptions
    {
        RootDirectory = Path.Combine(
            sp.GetRequiredService<ServerRuntimeOptions>().DataPath,
            "agent-shell")
    });
    services.AddSingleton<AgentShellService>();

    if (app.Mode != AppRunMode.Stdio)
        services.AddHostedService<KnowledgeCondenseScheduler>();

    var jwtService = new JwtService();
    services.AddSingleton(jwtService);
    services.AddSingleton<UserStore>(sp =>
    {
        var store = new UserStore();
        store.Initialize(sp.GetRequiredService<ServerRuntimeOptions>().DataPath);
        return store;
    });
    services.AddSingleton<MemoryReviewStore>(sp =>
    {
        var store = new MemoryReviewStore();
        store.Initialize(sp.GetRequiredService<ServerRuntimeOptions>().DataPath);
        return store;
    });

    services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(opts => opts.TokenValidationParameters = jwtService.GetValidationParameters());
    services.AddAuthorization(ServerPolicies.Configure);
});

app.ConfigureWebApp(web =>
{
    web.UseMiddleware<RequestLoggingMiddleware>();
    web.UseAuthentication();
    web.UseAuthorization();
    web.MapAuthEndpoints();
    web.MapApiEndpoints(DateTime.UtcNow);
});

return await app.RunAsync();
