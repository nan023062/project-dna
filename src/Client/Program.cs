using Avalonia;
using Dna.Client.Desktop;
using Dna.Client.Services;
using Dna.Core.Framework;
using Dna.Core.Runtime;

return await ProgramEntry.MainAsync(args);

internal static class ProgramEntry
{
    [STAThread]
    public static async Task<int> MainAsync(string[] args)
    {
        if (ShouldRunHeadless(args))
            return await RunHeadlessAsync(args);

        return RunDesktop(args);
    }

    private static bool ShouldRunHeadless(string[] args)
    {
        if (args.Length == 0)
            return false;

        var first = args[0];
        if (IsArg(first, "desktop", "--desktop"))
            return false;

        if (IsArg(first, "cli", "stdio", "--stdio", "web", "serve"))
            return true;

        return args.Any(a =>
        {
            return IsArg(a,
                "--server",
                "--workspace-root",
                "--workspace-config",
                "--port",
                "-p",
                "--no-browser");
        });
    }

    private static async Task<int> RunHeadlessAsync(string[] args)
    {
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

        ClientHostComposition.ConfigureDnaApp(
            app,
            serverBaseUrl,
            workspaceRoot,
            workspaceConfigPath);

        return await app.RunAsync();
    }

    private static int RunDesktop(string[] args)
    {
        var desktopArgs = args
            .Where(a => !IsArg(a, "desktop", "--desktop"))
            .ToArray();

        if (!SingleInstanceLock.TryAcquire("project-dna-client", out var instanceLock, out var lockName))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[Project DNA Client] 已有实例在运行（lock={lockName}）。");
            Console.ResetColor();
            return 1;
        }

        using (instanceLock)
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(desktopArgs);
        }

        return 0;
    }

    private static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static bool IsArg(string? value, params string[] candidates)
        => value != null &&
           Array.Exists(candidates, candidate => string.Equals(value, candidate, StringComparison.OrdinalIgnoreCase));
}
