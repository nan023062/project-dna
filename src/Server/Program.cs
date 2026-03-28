using Dna.Core.Config;
using Dna.Core.Framework;
using Dna.Interfaces.Api;
using Dna.Interfaces.Cli;
using Dna.Adapters.Game;
using Dna.Knowledge;

// ── 创建应用 ──
DnaApp.Create(args, new AppOptions
{
    AppName = "Project DNA",
    AppDescription = "工作区引擎",
    DefaultPort = 5051,
    LockScopeProvider = sp =>
    {
        var config = sp.GetRequiredService<ProjectConfig>();
        return config.HasProject ? config.DefaultProjectRoot : Directory.GetCurrentDirectory();
    },
    LogDirectoryProvider = sp =>
    {
        var config = sp.GetRequiredService<ProjectConfig>();
        return config.HasProject ? config.DefaultProjectRoot : null;
    },
    BannerExtras = (sp, port) =>
    {
        var config = sp.GetRequiredService<ProjectConfig>();
        var lines = new List<(string, string)>
        {
            ("REST API:    ", $"http://localhost:{port}/api/"),
            ("MCP Server:  ", $"http://localhost:{port}/mcp")
        };
        if (config.HasProject)
            lines.Add(("项目根目录:  ", config.DefaultProjectRoot));
        return lines;
    }
});

// ── CLI 命令 ──
DnaApp.AddCliCommand(new DefaultCliCommand());

// ── 注册服务 ──
// TODO: 逐步将这些服务改造为 IDnaService 后，改用 DnaApp.Register<T>()
DnaApp.ConfigureServices(services =>
{
    services.AddSingleton<ProjectConfig>();
    services.AddSingleton<GameProjectAdapter>();
    services.AddSingleton<IProjectAdapter>(sp => sp.GetRequiredService<GameProjectAdapter>());
    services.AddKnowledgeGraph();

    var personaName = ResolvePersonaName(services);
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
    web.MapApiEndpoints(DateTime.UtcNow);
    web.MapMcp("/mcp");
});

return await DnaApp.RunAsync();

static string ResolvePersonaName(IServiceCollection services)
{
    try
    {
        using var sp = services.BuildServiceProvider();
        var config = sp.GetService<ProjectConfig>();
        if (config is { HasProject: true })
        {
            var archPath = Path.Combine(config.DefaultProjectRoot, ".dna", "architecture.json");
            if (File.Exists(archPath))
            {
                var json = File.ReadAllText(archPath);
                var opts = new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                };
                var arch = System.Text.Json.JsonSerializer.Deserialize<Dna.Knowledge.Project.Models.ArchitectureManifest>(json, opts);
                if (!string.IsNullOrWhiteSpace(arch?.Persona?.Name))
                    return arch.Persona.Name;
            }
        }
    }
    catch { /* best effort */ }
    return "Project DNA";
}
