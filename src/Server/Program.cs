using Dna.Adapters.Game;
using Dna.Auth;
using Dna.Core.Config;
using Dna.Core.Framework;
using Dna.Interfaces.Api;
using Dna.Interfaces.Cli;
using Dna.Knowledge;
using Dna.Services;
using Dna.Web.Shared.AgentShell;

var runtimeOptions = ServerBootstrap.CreateRuntimeOptions(args);
var appArgs = ServerBootstrap.SanitizeArgsForFixedPort(args);

var app = DnaApp.Create(appArgs, new AppOptions
{
    AppName = "Project DNA",
    AppDescription = "项目认知引擎",
    DefaultPort = 5051,
    AllowPortAutoFallback = false,
    LockScopeProvider = _ => "project-dna-server",
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

    services.AddSingleton<ServerAllowlistStore>();
    services.AddAuthentication();
    services.AddAuthorization(ServerPolicies.Configure);
});

app.ConfigureWebApp(web =>
{
    web.UseMiddleware<RequestLoggingMiddleware>();
    web.UseMiddleware<ServerAccessControlMiddleware>();
    web.UseAuthentication();
    web.UseAuthorization();
    web.MapApiEndpoints(DateTime.UtcNow);
});

return await app.RunAsync();
