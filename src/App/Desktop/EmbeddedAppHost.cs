using Dna.Core.Logging;
using Dna.App.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace Dna.App.Desktop;

public sealed class EmbeddedAppHost
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    private WebApplication? _webApp;
    private CancellationTokenSource? _cts;
    private Task? _runTask;
    private DesktopProjectConfig? _currentProject;

    public bool IsRunning => _runTask is { IsCompleted: false };
    public DesktopProjectConfig? CurrentProject => _currentProject;

    public async Task StartAsync(DesktopProjectConfig project)
    {
        await _gate.WaitAsync();
        try
        {
            if (IsRunning)
            {
                if (string.Equals(_currentProject?.ProjectRoot, project.ProjectRoot, StringComparison.OrdinalIgnoreCase))
                    return;

                throw new InvalidOperationException("App 正在运行其他项目，请先停止后再启动新项目。");
            }

            project.EnsureProjectScopedAppState();
            AppDesktopLog.ConfigureProject(project);
            var logger = AppDesktopLog.CreateLogger<EmbeddedAppHost>();

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                ContentRootPath = AppContext.BaseDirectory
            });
            builder.WebHost.UseUrls("http://0.0.0.0:5052");
            AppDesktopLog.ConfigureAspNetLogging(builder.Logging);

            AppHostComposition.ConfigureServices(
                builder.Services,
                project.ProjectName,
                project.ProjectRoot,
                project.MetadataRootPath,
                null);

            var app = builder.Build();
            AppHostComposition.ConfigureWebApp(app);

            _webApp = app;
            _cts = new CancellationTokenSource();
            _runTask = app.RunAsync(_cts.Token);
            _currentProject = project;

            await WaitUntilOnlineAsync();
            logger.LogInformation(
                LogEvents.Workspace,
                "Desktop App host started: project={ProjectName}, root={ProjectRoot}, runtime={RuntimeBaseUrl}, logDir={LogDirectory}",
                project.ProjectName,
                project.ProjectRoot,
                AppRuntimeConstants.ApiBaseUrl,
                project.LogDirectoryPath);
        }
        catch
        {
            await StopInternalAsync();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await StopInternalAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task StopInternalAsync()
    {
        var logger = AppDesktopLog.CreateLogger<EmbeddedAppHost>();
        var previousProject = _currentProject;
        _currentProject = null;

        var app = _webApp;
        var cts = _cts;
        var runTask = _runTask;

        _webApp = null;
        _cts = null;
        _runTask = null;

        if (cts is not null)
            await cts.CancelAsync();

        if (app is not null)
        {
            try
            {
                await app.StopAsync(TimeSpan.FromSeconds(3));
            }
            catch
            {
                // best effort
            }

            await app.DisposeAsync();
        }

        if (runTask is not null)
        {
            try
            {
                await runTask;
            }
            catch (OperationCanceledException)
            {
                // expected
            }
            catch
            {
                // best effort
            }
        }

        cts?.Dispose();

        if (previousProject is not null)
        {
            logger.LogInformation(
                LogEvents.Workspace,
                "Desktop App host stopped: project={ProjectName}, root={ProjectRoot}",
                previousProject.ProjectName,
                previousProject.ProjectRoot);
        }
    }

    private static async Task WaitUntilOnlineAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(400) };
        for (var i = 0; i < 40; i++)
        {
            try
            {
                using var resp = await http.GetAsync("http://127.0.0.1:5052/api/app/status");
                if (resp.IsSuccessStatusCode)
                    return;
            }
            catch
            {
                // retry
            }

            await Task.Delay(200);
        }

        throw new InvalidOperationException("App 进程已启动，但 5052 未在预期时间内就绪。");
    }
}
