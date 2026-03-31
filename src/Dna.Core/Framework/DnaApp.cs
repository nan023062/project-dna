using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Dna.Core.Logging;
using Dna.Core.Runtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dna.Core.Framework;

public enum AppRunMode { Cli, Stdio, Web }

/// <summary>
/// 应用管理器（静态类）— 封装所有服务的注册、生命周期管理和应用启动流程。
///
/// 用法：
///   DnaApp.Create(args, new AppOptions { AppName = "MyApp" });
///   DnaApp.Register&lt;MyService&gt;();
///   DnaApp.ConfigureWebApp(web => { ... });
///   return await DnaApp.RunAsync();
/// </summary>
public static class DnaApp
{
    private static string[] _args = [];
    private static AppOptions _options = new();
    private static AppRunMode _mode;
    private static FileLogWriter _fileWriter = new();

    private static readonly List<ServiceDescriptor> _serviceRegistrations = [];
    private static readonly List<ICliCommand> _cliCommands = [];
    private static readonly List<Action<IServiceCollection>> _rawConfigurators = [];
    private static readonly List<Action<WebApplication>> _webConfigurators = [];

    private static IServiceProvider? _serviceProvider;
    private static readonly List<IDnaService> _managedServices = [];

    private static int _port;
    private static bool _openBrowser;

    public static AppRunMode Mode => _mode;
    public static AppOptions Options => _options;

    // ═══════════════════════════════════════════
    //  创建
    // ═══════════════════════════════════════════

    /// <summary>初始化应用管理器</summary>
    public static void Create(string[] args, AppOptions? options = null)
    {
        _args = args;
        _options = options ?? new AppOptions();
        _mode = DetectMode(args);
        _port = _options.DefaultPort;
        _openBrowser = _options.OpenBrowser;
        _fileWriter = new FileLogWriter();
    }

    // ═══════════════════════════════════════════
    //  服务注册（必须实现 IDnaService）
    // ═══════════════════════════════════════════

    /// <summary>注册服务（Singleton，自动管理生命周期）</summary>
    public static void Register<T>() where T : class, IDnaService
    {
        _serviceRegistrations.Add(ServiceDescriptor.Singleton<T, T>());
    }

    /// <summary>注册服务（接口 + 实现分离）</summary>
    public static void Register<TInterface, TImpl>()
        where TInterface : class
        where TImpl : class, TInterface, IDnaService
    {
        _serviceRegistrations.Add(ServiceDescriptor.Singleton<TImpl, TImpl>());
        _serviceRegistrations.Add(ServiceDescriptor.Singleton<TInterface>(sp => sp.GetRequiredService<TImpl>()));
    }

    /// <summary>注册已创建的服务实例</summary>
    public static void Register<T>(T instance) where T : class, IDnaService
    {
        _serviceRegistrations.Add(ServiceDescriptor.Singleton(instance));
    }

    /// <summary>注册框架级服务（不要求 IDnaService，用于 MCP、HttpClient 等）</summary>
    public static void ConfigureServices(Action<IServiceCollection> configure)
    {
        _rawConfigurators.Add(configure);
    }

    // ═══════════════════════════════════════════
    //  CLI 命令注册
    // ═══════════════════════════════════════════

    public static void AddCliCommand(ICliCommand command) => _cliCommands.Add(command);
    public static void AddCliCommand<T>() where T : ICliCommand, new() => _cliCommands.Add(new T());

    // ═══════════════════════════════════════════
    //  Web 管道配置
    // ═══════════════════════════════════════════

    public static void ConfigureWebApp(Action<WebApplication> configure) => _webConfigurators.Add(configure);

    // ═══════════════════════════════════════════
    //  服务访问（应用启动后可用）
    // ═══════════════════════════════════════════

    /// <summary>获取已注册的服务实例</summary>
    public static T GetService<T>() where T : class
        => (_serviceProvider ?? throw new InvalidOperationException("App not started")).GetRequiredService<T>();

    /// <summary>尝试获取服务实例</summary>
    public static T? TryGetService<T>() where T : class
        => _serviceProvider?.GetService<T>();

    // ═══════════════════════════════════════════
    //  启动
    // ═══════════════════════════════════════════

    public static async Task<int> RunAsync()
    {
        return _mode switch
        {
            AppRunMode.Cli => await RunCliAsync(),
            AppRunMode.Stdio => await RunStdioAsync(),
            _ => await RunWebAsync()
        };
    }

    // ═══════════════════════════════════════════
    //  CLI 模式
    // ═══════════════════════════════════════════

    private static async Task<int> RunCliAsync()
    {
        if (_cliCommands.Count == 0)
        {
            Console.WriteLine($"{_options.AppName}: no CLI commands registered.");
            return 1;
        }

        var subcommand = _args.Length > 1 ? _args[1] : "help";
        var restArgs = _args.Length > 1 ? _args[1..] : [];

        if (subcommand is "help" or "--help" or "-h")
        {
            PrintCliHelp();
            return 0;
        }

        var command = _cliCommands.FirstOrDefault(c =>
            string.Equals(c.Name, subcommand, StringComparison.OrdinalIgnoreCase));

        if (command != null)
            return await command.ExecuteAsync(restArgs);

        if (_cliCommands.Count == 1)
            return await _cliCommands[0].ExecuteAsync(_args);

        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Unknown command: {subcommand}");
        Console.ResetColor();
        PrintCliHelp();
        return 1;
    }

    private static void PrintCliHelp()
    {
        Console.WriteLine($"{_options.AppName} CLI");
        Console.WriteLine();
        Console.WriteLine("Usage: <app> cli <command> [args...]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        var maxLen = _cliCommands.Max(c => c.Name.Length);
        foreach (var cmd in _cliCommands)
            Console.WriteLine($"  {cmd.Name.PadRight(maxLen + 2)}{cmd.Description}");
    }

    // ═══════════════════════════════════════════
    //  stdio 模式
    // ═══════════════════════════════════════════

    private static async Task<int> RunStdioAsync()
    {
        var builder = Host.CreateApplicationBuilder(
            _args.Where(a => !IsArg(a, "--stdio", "stdio")).ToArray());

        ConfigureLogging(builder.Services, builder.Logging, useStdErr: true);
        ApplyAllServices(builder.Services);

        var host = builder.Build();
        _serviceProvider = host.Services;

        return await StartWithLockAsync(async () =>
        {
            await InitializeServicesAsync();
            await host.RunAsync();
        });
    }

    // ═══════════════════════════════════════════
    //  Web 模式
    // ═══════════════════════════════════════════

    private static async Task<int> RunWebAsync()
    {
        ParseWebArgs();
        _port = FindAvailablePort(_port);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = AppContext.BaseDirectory
        });

        ConfigureLogging(builder.Services, builder.Logging, useStdErr: false);
        builder.WebHost.UseUrls($"http://0.0.0.0:{_port}");
        ApplyAllServices(builder.Services);

        var webApp = builder.Build();
        _serviceProvider = webApp.Services;

        ConfigureStaticFiles(webApp);
        foreach (var configure in _webConfigurators)
            configure(webApp);

        return await StartWithLockAsync(async () =>
        {
            await InitializeServicesAsync();
            PrintBanner(_port);
            if (_openBrowser) LaunchBrowser(_port);
            await webApp.RunAsync();
        });
    }

    // ═══════════════════════════════════════════
    //  服务注册（内部）
    // ═══════════════════════════════════════════

    private static void ApplyAllServices(IServiceCollection services)
    {
        services.AddHttpClient();

        foreach (var descriptor in _serviceRegistrations)
            services.Add(descriptor);

        foreach (var configure in _rawConfigurators)
            configure(services);
    }

    // ═══════════════════════════════════════════
    //  生命周期管理
    // ═══════════════════════════════════════════

    private static async Task InitializeServicesAsync()
    {
        var provider = _serviceProvider ?? throw new InvalidOperationException("ServiceProvider is not initialized.");
        _managedServices.Clear();
        foreach (var svc in provider.GetServices<IDnaService>())
            _managedServices.Add(svc);

        // 同时发现通过 Register<T>() 注册的、但不是通过 IDnaService 接口查找到的服务
        foreach (var descriptor in _serviceRegistrations)
        {
            var implType = descriptor.ImplementationType ?? descriptor.ServiceType;
            if (typeof(IDnaService).IsAssignableFrom(implType))
            {
                var instance = provider.GetService(descriptor.ServiceType);
                if (instance is IDnaService svc && !_managedServices.Contains(svc))
                    _managedServices.Add(svc);
            }
        }

        var logger = provider.GetService<ILogger<FileLogWriter>>();
        foreach (var svc in _managedServices)
        {
            await svc.InitializeAsync();
            logger?.LogDebug("[DnaApp] {Service} initialized", svc.ServiceName);
        }

        logger?.LogInformation("[DnaApp] {Count} services initialized", _managedServices.Count);
    }

    private static async Task ShutdownServicesAsync()
    {
        for (var i = _managedServices.Count - 1; i >= 0; i--)
        {
            try { await _managedServices[i].ShutdownAsync(); }
            catch { /* best effort */ }
        }
        _managedServices.Clear();
    }

    // ═══════════════════════════════════════════
    //  单实例锁
    // ═══════════════════════════════════════════

    private static async Task<int> StartWithLockAsync(Func<Task> run)
    {
        var lockScope = _options.LockScopeProvider?.Invoke(_serviceProvider!) ?? Directory.GetCurrentDirectory();

        if (!SingleInstanceLock.TryAcquire(lockScope, out var instanceLock, out var lockName))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[{_options.AppName}] Instance lock conflict: another process is running (lock={lockName})");
            Console.ResetColor();
            return 1;
        }

        using (instanceLock)
        {
            var logDir = _options.LogDirectoryProvider?.Invoke(_serviceProvider!);
            if (logDir != null)
                _fileWriter.SetLogDirectory(logDir);

            if (_options.OnStarted != null)
                await _options.OnStarted(_serviceProvider!);

            try
            {
                await run();
            }
            finally
            {
                await ShutdownServicesAsync();
            }
            return 0;
        }
    }

    // ═══════════════════════════════════════════
    //  日志
    // ═══════════════════════════════════════════

    private static void ConfigureLogging(IServiceCollection services, ILoggingBuilder logging, bool useStdErr)
    {
        services.AddSingleton(_fileWriter);
        logging.ClearProviders();
        logging.AddProvider(new DnaLoggerProvider(_fileWriter, useStdErr: useStdErr));
        logging.SetMinimumLevel(LogLevel.Information);
        logging.AddFilter("Microsoft", LogLevel.Warning);
        logging.AddFilter("System", LogLevel.Warning);
    }

    // ═══════════════════════════════════════════
    //  静态文件
    // ═══════════════════════════════════════════

    private static void ConfigureStaticFiles(WebApplication app)
    {
        var wwwroot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        if (!Directory.Exists(wwwroot))
            wwwroot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        if (!Directory.Exists(wwwroot)) return;

        // Shared static assets are referenced by Server/Client pages via /dna-shared/*.
        // We map it explicitly so both "dotnet run" and published binaries can resolve assets.
        var sharedWebRoot = ResolveSharedWebRoot(wwwroot);
        if (sharedWebRoot != null)
        {
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(sharedWebRoot),
                RequestPath = "/dna-shared",
                OnPrepareResponse = ctx =>
                {
                    ctx.Context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
                    ctx.Context.Response.Headers["Pragma"] = "no-cache";
                    ctx.Context.Response.Headers["Expires"] = "0";
                }
            });
        }

        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = new PhysicalFileProvider(wwwroot) });
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(wwwroot),
            RequestPath = "",
            OnPrepareResponse = ctx =>
            {
                ctx.Context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
                ctx.Context.Response.Headers["Pragma"] = "no-cache";
                ctx.Context.Response.Headers["Expires"] = "0";
            }
        });
        app.MapGet("/", () =>
        {
            var indexPath = Path.Combine(wwwroot, "index.html");
            return !File.Exists(indexPath)
                ? Results.NotFound("index.html not found")
                : Results.File(indexPath, "text/html; charset=utf-8");
        });
    }

    private static string? ResolveSharedWebRoot(string appWebRoot)
    {
        var candidates = new[]
        {
            // Published layout (if copied into app wwwroot/dna-shared).
            Path.Combine(appWebRoot, "dna-shared"),
            // Repo layout when running publish binary from project-dna/publish/{app}.
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "src", "Dna.Web.Shared", "wwwroot")),
            // Repo layout when running from source directory.
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "Dna.Web.Shared", "wwwroot")),
            // Fallback: relative to app base.
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "Dna.Web.Shared", "wwwroot"))
        };

        return candidates.FirstOrDefault(Directory.Exists);
    }

    // ═══════════════════════════════════════════
    //  横幅
    // ═══════════════════════════════════════════

    private static void PrintBanner(int port)
    {
        var name = _options.AppName;
        var desc = string.IsNullOrEmpty(_options.AppDescription) ? "" : $" — {_options.AppDescription}";
        var title = $"{name}{desc}";
        var boxWidth = Math.Max(title.Length + 8, 40);
        var padded = title.PadLeft((boxWidth - 2 + title.Length) / 2).PadRight(boxWidth - 2);

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"  ╔{new string('═', boxWidth)}╗");
        Console.WriteLine($"  ║{padded}║");
        Console.WriteLine($"  ╚{new string('═', boxWidth)}╝");
        Console.ResetColor();
        Console.WriteLine();

        PrintLine("Web:         ", $"http://localhost:{port}");

        var extras = _options.BannerExtras?.Invoke(_serviceProvider!, port);
        if (extras != null)
            foreach (var (label, value) in extras)
                PrintLine(label, value);

        if (port != _options.DefaultPort)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"  * Port {_options.DefaultPort} is in use, switched to {port}");
            Console.ResetColor();
        }
        Console.WriteLine();
    }

    private static void PrintLine(string label, string value)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write($"  {label}");
        Console.ResetColor();
        Console.WriteLine(value);
    }

    private static void LaunchBrowser(int port)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(1200);
            try { Process.Start(new ProcessStartInfo { FileName = $"http://localhost:{port}", UseShellExecute = true }); }
            catch { /* ignore */ }
        });
    }

    // ═══════════════════════════════════════════
    //  参数解析
    // ═══════════════════════════════════════════

    private static void ParseWebArgs()
    {
        for (var i = 0; i < _args.Length; i++)
        {
            if (IsArg(_args[i], "--port", "-p") && i + 1 < _args.Length)
                int.TryParse(_args[++i], out _port);
            else if (IsArg(_args[i], "--no-browser"))
                _openBrowser = false;
        }
    }

    private static AppRunMode DetectMode(string[] args)
    {
        var arg0 = args.Length > 0 ? args[0] : null;
        if (IsArg(arg0, "cli")) return AppRunMode.Cli;
        if (IsArg(arg0, "--stdio", "stdio")) return AppRunMode.Stdio;
        return AppRunMode.Web;
    }

    private static bool IsArg(string? a, params string[] values)
        => a != null && Array.Exists(values, v => string.Equals(a, v, StringComparison.OrdinalIgnoreCase));

    private static int FindAvailablePort(int preferred)
    {
        if (IsPortAvailable(preferred)) return preferred;
        for (var p = preferred + 1; p <= preferred + 50; p++)
            if (IsPortAvailable(p)) return p;
        using var listener = new TcpListener(IPAddress.Any, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static bool IsPortAvailable(int port)
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch { return false; }
    }
}
