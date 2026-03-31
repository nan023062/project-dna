using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Client.Desktop;

public partial class MainWindow : Window
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMilliseconds(900) };
    private readonly DispatcherTimer _statusTimer;
    private Process? _clientProcess;
    private LaunchPlan? _launchPlan;

    public MainWindow()
    {
        InitializeComponent();
        InitializeDefaults();

        _statusTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _statusTimer.Tick += async (_, _) => await RefreshRuntimeStatusAsync();
        _statusTimer.Start();

        Opened += async (_, _) => await RefreshRuntimeStatusAsync();
        Closed += (_, _) => _statusTimer.Stop();
    }

    private void InitializeDefaults()
    {
        ServerBaseUrlBox.Text = "http://127.0.0.1:5051";
        ClientAddressBox.Text = "http://127.0.0.1:5052";
        McpAddressBox.Text = "http://127.0.0.1:5052/mcp";
        WorkspaceRootBox.Text = ResolveDefaultWorkspaceRoot();
        StatusText.Text = "准备就绪。";
    }

    private async void StartClient_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (await IsClientOnlineAsync())
            {
                StatusText.Text = "Client 已在 5052 运行，无需重复启动。";
                await RefreshRuntimeStatusAsync();
                return;
            }

            _launchPlan ??= ResolveLaunchPlan();
            var server = NormalizeUrl(ServerBaseUrlBox.Text, "server 地址不能为空。");
            var workspace = NormalizeWorkspace(WorkspaceRootBox.Text);

            if (!Directory.Exists(workspace))
                throw new InvalidOperationException($"工作区路径不存在：{workspace}");

            var process = new Process
            {
                StartInfo = BuildClientStartInfo(_launchPlan, server, workspace),
                EnableRaisingEvents = true
            };
            process.Exited += (_, _) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (ReferenceEquals(_clientProcess, process))
                        _clientProcess = null;
                    StatusText.Text = "Client 进程已退出。";
                });
            };

            if (!process.Start())
                throw new InvalidOperationException("Client 进程启动失败。");

            _clientProcess = process;
            StatusText.Text = $"Client 已启动（PID={process.Id}）。";
            await WaitClientOnlineAsync();
            await RefreshRuntimeStatusAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"启动失败：{ex.Message}";
            await RefreshRuntimeStatusAsync();
        }
    }

    private async void StopClient_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (_clientProcess is not { HasExited: false })
            {
                StatusText.Text = "当前没有由桌面壳启动的 Client 进程。";
                return;
            }

            _clientProcess.Kill(entireProcessTree: true);
            _clientProcess = null;
            StatusText.Text = "已停止 Client 进程。";
            await RefreshRuntimeStatusAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"停止失败：{ex.Message}";
        }
    }

    private async void OpenWorkbench_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var clientUrl = NormalizeUrl(ClientAddressBox.Text, "Client 地址不能为空。");
            Process.Start(new ProcessStartInfo
            {
                FileName = clientUrl,
                UseShellExecute = true
            });
            StatusText.Text = $"已打开工作台：{clientUrl}";
            await RefreshRuntimeStatusAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"打开失败：{ex.Message}";
        }
    }

    private async void CopyMcp_OnClick(object? sender, RoutedEventArgs e)
    {
        var mcpAddress = NormalizeUrl(McpAddressBox.Text, "MCP 地址不能为空。");
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(mcpAddress);
            StatusText.Text = $"MCP 地址已复制：{mcpAddress}";
            return;
        }

        StatusText.Text = "无法访问系统剪贴板。";
    }

    private async void RefreshStatus_OnClick(object? sender, RoutedEventArgs e)
    {
        await RefreshRuntimeStatusAsync();
    }

    private async Task RefreshRuntimeStatusAsync()
    {
        var clientOnline = await IsClientOnlineAsync();
        ClientStateText.Text = clientOnline
            ? "Client: 在线（http://127.0.0.1:5052）"
            : "Client: 离线";

        var serverOnline = await IsServerOnlineAsync();
        ServerStateText.Text = serverOnline
            ? "Server: 在线（http://127.0.0.1:5051）"
            : $"Server: 离线（{NormalizeUrl(ServerBaseUrlBox.Text, "http://127.0.0.1:5051")}）";

        try
        {
            _launchPlan ??= ResolveLaunchPlan();
            LaunchModeText.Text = _launchPlan.Mode == LaunchMode.PublishedDll
                ? $"启动方式: dotnet {_launchPlan.ClientDllPath}"
                : $"启动方式: dotnet run --project {_launchPlan.ClientProjectPath}";
        }
        catch (Exception ex)
        {
            LaunchModeText.Text = $"启动方式: 未就绪（{ex.Message}）";
        }
    }

    private static async Task<bool> IsClientOnlineAsync()
    {
        try
        {
            using var response = await Http.GetAsync("http://127.0.0.1:5052/api/client/status");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> IsServerOnlineAsync()
    {
        try
        {
            var server = NormalizeUrl(ServerBaseUrlBox.Text, "http://127.0.0.1:5051");
            using var response = await Http.GetAsync($"{server}/api/status");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private async Task WaitClientOnlineAsync()
    {
        for (var i = 0; i < 30; i++)
        {
            if (await IsClientOnlineAsync())
                return;

            await Task.Delay(300);
        }
    }

    private static ProcessStartInfo BuildClientStartInfo(LaunchPlan plan, string serverBaseUrl, string workspaceRoot)
    {
        var info = new ProcessStartInfo
        {
            FileName = "dotnet",
            UseShellExecute = false
        };

        if (plan.Mode == LaunchMode.PublishedDll)
        {
            info.ArgumentList.Add(plan.ClientDllPath!);
            info.ArgumentList.Add("--workspace-root");
            info.ArgumentList.Add(workspaceRoot);
            info.ArgumentList.Add("--server");
            info.ArgumentList.Add(serverBaseUrl);
            info.WorkingDirectory = Path.GetDirectoryName(plan.ClientDllPath)!;
            return info;
        }

        info.ArgumentList.Add("run");
        info.ArgumentList.Add("--project");
        info.ArgumentList.Add(plan.ClientProjectPath!);
        info.ArgumentList.Add("--");
        info.ArgumentList.Add("--workspace-root");
        info.ArgumentList.Add(workspaceRoot);
        info.ArgumentList.Add("--server");
        info.ArgumentList.Add(serverBaseUrl);
        info.WorkingDirectory = plan.RepoRoot!;
        return info;
    }

    private static string NormalizeUrl(string? raw, string fallback)
    {
        var value = string.IsNullOrWhiteSpace(raw) ? fallback : raw.Trim();
        return value.TrimEnd('/');
    }

    private static string NormalizeWorkspace(string? raw)
    {
        var value = string.IsNullOrWhiteSpace(raw)
            ? ResolveDefaultWorkspaceRoot()
            : raw.Trim();

        return Path.GetFullPath(value);
    }

    private static string ResolveDefaultWorkspaceRoot()
    {
        var repoRoot = TryResolveRepoRoot();
        if (repoRoot is not null)
        {
            var parent = Directory.GetParent(repoRoot)?.FullName;
            if (!string.IsNullOrWhiteSpace(parent))
                return parent;

            return repoRoot;
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    private static LaunchPlan ResolveLaunchPlan()
    {
        var repoRoot = TryResolveRepoRoot()
            ?? throw new InvalidOperationException("未找到 project-dna 仓库根目录。");

        var publishedDll = Path.Combine(repoRoot, "publish", "client", "dna_client.dll");
        if (File.Exists(publishedDll))
        {
            return new LaunchPlan
            {
                Mode = LaunchMode.PublishedDll,
                ClientDllPath = publishedDll,
                RepoRoot = repoRoot
            };
        }

        var projectPath = Path.Combine(repoRoot, "src", "Client", "Client.csproj");
        if (!File.Exists(projectPath))
            throw new InvalidOperationException("未找到 src/Client/Client.csproj。");

        return new LaunchPlan
        {
            Mode = LaunchMode.DotnetRunProject,
            ClientProjectPath = projectPath,
            RepoRoot = repoRoot
        };
    }

    private static string? TryResolveRepoRoot()
    {
        var baseDir = AppContext.BaseDirectory;
        foreach (var candidate in EnumerateParents(baseDir))
        {
            if (File.Exists(Path.Combine(candidate, "src", "Client", "Client.csproj")))
                return candidate;
        }

        var cwd = Directory.GetCurrentDirectory();
        foreach (var candidate in EnumerateParents(cwd))
        {
            if (File.Exists(Path.Combine(candidate, "src", "Client", "Client.csproj")))
                return candidate;
        }

        return null;
    }

    private static IEnumerable<string> EnumerateParents(string start)
    {
        var current = new DirectoryInfo(start);
        while (current is not null)
        {
            yield return current.FullName;
            current = current.Parent;
        }
    }

    private sealed class LaunchPlan
    {
        public LaunchMode Mode { get; init; }
        public string? ClientProjectPath { get; init; }
        public string? ClientDllPath { get; init; }
        public string? RepoRoot { get; init; }
    }

    private enum LaunchMode
    {
        DotnetRunProject,
        PublishedDll
    }
}
