using Dna.Core.Framework;
using Dna.Core.Runtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Dna.Client.Desktop;

public sealed class EmbeddedClientHost
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

                throw new InvalidOperationException("Client 正在运行其他项目，请先停止后再启动新项目。");
            }

            project.EnsureWorkspaceConfig();

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                ContentRootPath = AppContext.BaseDirectory
            });
            builder.WebHost.UseUrls("http://0.0.0.0:5052");

            ClientHostComposition.ConfigureServices(
                builder.Services,
                AppRunMode.Web,
                project.ServerBaseUrl,
                project.ProjectRoot,
                project.WorkspaceConfigPath);

            var app = builder.Build();
            ClientHostComposition.ConfigureWebApp(app);

            _webApp = app;
            _cts = new CancellationTokenSource();
            _runTask = app.RunAsync(_cts.Token);
            _currentProject = project;

            await WaitUntilOnlineAsync();
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
    }

    private static async Task WaitUntilOnlineAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(400) };
        for (var i = 0; i < 40; i++)
        {
            try
            {
                using var resp = await http.GetAsync("http://127.0.0.1:5052/api/client/status");
                if (resp.IsSuccessStatusCode)
                    return;
            }
            catch
            {
                // retry
            }

            await Task.Delay(200);
        }

        throw new InvalidOperationException("Client 进程已启动，但 5052 未在预期时间内就绪。");
    }
}
