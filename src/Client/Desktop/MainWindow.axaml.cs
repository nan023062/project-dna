using System.Net.Http;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace Dna.Client.Desktop;

public partial class MainWindow : Window
{
    private static readonly HttpClient StatusHttp = new() { Timeout = TimeSpan.FromMilliseconds(900) };
    private static readonly HttpClient ApiHttp = new() { Timeout = TimeSpan.FromSeconds(30) };

    private readonly EmbeddedClientHost _host;
    private readonly DispatcherTimer _statusTimer;

    private DesktopProjectConfig? _project;

    public MainWindow()
        : this(new EmbeddedClientHost())
    {
    }

    public MainWindow(EmbeddedClientHost host)
    {
        _host = host;
        InitializeComponent();
        InitializeDefaults();

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _statusTimer.Tick += async (_, _) => await RefreshRuntimeStatusAsync();
        _statusTimer.Start();

        Opened += async (_, _) => await RefreshAllAsync();
        Closed += (_, _) => _statusTimer.Stop();
    }

    private void InitializeDefaults()
    {
        ProjectRootBox.Text = string.Empty;
        ProjectNameBox.Text = string.Empty;
        ServerBaseUrlBox.Text = "-";
        ClientAddressBox.Text = "http://127.0.0.1:5052";
        McpAddressBox.Text = "http://127.0.0.1:5052/mcp";
        ProjectConfigPathText.Text = "配置文件: 未选择项目";

        SelectedProjectText.Text = "目标项目: 未选择";
        ClientStateText.Text = "Client: 检查中...";
        ServerStateText.Text = "Server: 检查中...";
        AccessStateText.Text = "权限: 检查中...";
        StatusText.Text = "请选择项目目录后启动 Client。";

        TopologySummaryText.Text = "尚未加载拓扑。";
        MemoryStatusText.Text = "记忆区就绪。";
        ToolingStatusText.Text = "工具区就绪。";
        McpSummaryText.Text = "MCP 清单未加载。";
    }

    private async void SelectProject_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var selected = await PickProjectFolderAsync();
            if (string.IsNullOrWhiteSpace(selected))
            {
                StatusText.Text = "已取消项目选择。";
                return;
            }

            var project = DesktopProjectConfig.Load(selected);
            project.EnsureWorkspaceConfig();
            ApplyProject(project);

            StatusText.Text = $"项目已就绪：{project.ProjectName}";
            await RefreshAllAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"项目配置无效：{ex.Message}";
            await RefreshRuntimeStatusAsync();
        }
    }

    private async void StartClient_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var project = EnsureProjectSelected();
            await _host.StartAsync(project);
            StatusText.Text = "Client 已启动（单进程内嵌）。";
            await RefreshClientPanelsAsync();
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
            await _host.StopAsync();
            StatusText.Text = "Client 已停止。";
            await RefreshClientPanelsAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"停止失败：{ex.Message}";
        }
    }

    private async void RefreshAll_OnClick(object? sender, RoutedEventArgs e)
    {
        await RefreshAllAsync();
    }

    private async void CopyMcp_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var text = (McpAddressBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidOperationException("MCP 地址为空。");

            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(text);
                StatusText.Text = $"MCP 地址已复制：{text}";
                return;
            }

            StatusText.Text = "无法访问系统剪贴板。";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"复制失败：{ex.Message}";
        }
    }

    private async void RefreshTopology_OnClick(object? sender, RoutedEventArgs e)
    {
        await RefreshTopologyAsync();
    }

    private async void RefreshMemories_OnClick(object? sender, RoutedEventArgs e)
    {
        await RefreshMemoriesAsync();
    }

    private async void AddMemory_OnClick(object? sender, RoutedEventArgs e)
    {
        await AddMemoryAsync();
    }

    private async void InstallCursor_OnClick(object? sender, RoutedEventArgs e)
    {
        await InstallToolingAsync("cursor");
    }

    private async void InstallCodex_OnClick(object? sender, RoutedEventArgs e)
    {
        await InstallToolingAsync("codex");
    }

    private async void RefreshTooling_OnClick(object? sender, RoutedEventArgs e)
    {
        await RefreshToolingStatusAsync();
    }

    private async void RefreshMcpTools_OnClick(object? sender, RoutedEventArgs e)
    {
        await RefreshMcpToolsAsync();
    }

    private async Task RefreshAllAsync()
    {
        await RefreshRuntimeStatusAsync();
        await RefreshTopologyAsync();
        await RefreshMemoriesAsync();
        await RefreshToolingStatusAsync();
        await RefreshMcpToolsAsync();
    }

    private async Task RefreshClientPanelsAsync()
    {
        await RefreshRuntimeStatusAsync();
        await RefreshToolingStatusAsync();
        await RefreshMcpToolsAsync();
    }

    private async Task RefreshRuntimeStatusAsync()
    {
        var clientOnline = await IsClientOnlineAsync();
        ClientStateText.Text = clientOnline
            ? "Client: 在线（http://127.0.0.1:5052）"
            : "Client: 离线";

        if (_project is null)
        {
            SelectedProjectText.Text = "目标项目: 未选择";
            ServerStateText.Text = "Server: 未选择项目";
            AccessStateText.Text = "权限: 未选择项目";
            return;
        }

        SelectedProjectText.Text = $"目标项目: {_project.ProjectName}";

        var serverOnline = await IsServerOnlineAsync(_project.ServerBaseUrl);
        ServerStateText.Text = serverOnline
            ? $"Server: 在线（{_project.ServerBaseUrl}）"
            : $"Server: 离线（{_project.ServerBaseUrl}）";

        if (!serverOnline)
        {
            AccessStateText.Text = "权限: Server 离线，无法读取。";
            return;
        }

        try
        {
            var access = await GetJsonAsync($"{_project.ServerBaseUrl}/api/connection/access");
            var allowed = access.TryGetProperty("allowed", out var allowedElement) &&
                          allowedElement.ValueKind == JsonValueKind.True;

            if (!allowed)
            {
                AccessStateText.Text = $"权限: 未授权（{GetString(access, "reason", "当前客户端未授权")}）";
                return;
            }

            var role = GetString(access, "role", "unknown");
            var name = GetString(access, "entryName", "-");
            var ip = GetString(access, "remoteIp", "-");
            AccessStateText.Text = $"权限: {role} | 白名单: {name} | IP: {ip}";
        }
        catch (Exception ex)
        {
            AccessStateText.Text = $"权限: 读取失败（{ex.Message}）";
        }
    }

    private async Task RefreshTopologyAsync()
    {
        if (_project is null)
        {
            TopologySummaryText.Text = "未选择项目，无法加载拓扑。";
            TopologyListBox.ItemsSource = Array.Empty<string>();
            return;
        }

        try
        {
            var topology = await GetJsonAsync($"{_project.ServerBaseUrl}/api/topology");
            TopologySummaryText.Text = GetString(topology, "summary", "拓扑已加载")!;

            var lines = new List<string>();
            if (topology.TryGetProperty("modules", out var modules) && modules.ValueKind == JsonValueKind.Array)
            {
                foreach (var module in modules.EnumerateArray())
                {
                    var name = GetString(module, "displayName", GetString(module, "name", "-"));
                    var typeLabel = GetString(module, "typeLabel", GetString(module, "type", "-"));
                    var discipline = GetString(module, "disciplineDisplayName", GetString(module, "discipline", "-"));
                    var relativePath = GetString(module, "relativePath", "-");

                    var depCount = 0;
                    if (module.TryGetProperty("dependencies", out var deps) && deps.ValueKind == JsonValueKind.Array)
                        depCount = deps.GetArrayLength();

                    lines.Add($"{name} | {typeLabel} | {discipline} | deps={depCount} | {relativePath}");
                }
            }

            TopologyListBox.ItemsSource = lines;
        }
        catch (Exception ex)
        {
            TopologySummaryText.Text = $"拓扑加载失败：{ex.Message}";
            TopologyListBox.ItemsSource = Array.Empty<string>();
        }
    }

    private async Task RefreshMemoriesAsync()
    {
        if (_project is null)
        {
            MemoryStatusText.Text = "未选择项目，无法加载记忆。";
            MemoryListBox.ItemsSource = Array.Empty<string>();
            return;
        }

        try
        {
            var result = await GetJsonAsync($"{_project.ServerBaseUrl}/api/memory/query?limit=40&offset=0");
            var entries = new List<(DateTime createdAt, string line)>();

            if (result.ValueKind == JsonValueKind.Array)
            {
                foreach (var memory in result.EnumerateArray())
                    entries.Add((ParseDate(memory, "createdAt"), BuildMemoryLine(memory)));
            }

            var ordered = entries.OrderByDescending(x => x.createdAt).Select(x => x.line).ToList();
            MemoryListBox.ItemsSource = ordered;
            MemoryStatusText.Text = $"已加载 {ordered.Count} 条记忆。";
        }
        catch (Exception ex)
        {
            MemoryStatusText.Text = $"记忆加载失败：{ex.Message}";
            MemoryListBox.ItemsSource = Array.Empty<string>();
        }
    }

    private async Task AddMemoryAsync()
    {
        if (_project is null)
        {
            MemoryStatusText.Text = "未选择项目，无法写入。";
            return;
        }

        try
        {
            var content = (MemoryContentBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(content))
                throw new InvalidOperationException("请输入要写入的记忆内容。");

            var discipline = (MemoryDisciplineBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(discipline))
                discipline = "engineering";

            var type = ResolveMemoryTypeValue();
            var tags = ParseTags(MemoryTagsBox.Text);

            var payload = new
            {
                type,
                source = 2,
                nodeType = 2,
                disciplines = new[] { discipline },
                tags,
                content
            };

            var saved = await PostJsonAsync($"{_project.ServerBaseUrl}/api/memory/remember", payload);
            var id = GetString(saved, "id", "-");

            MemoryContentBox.Text = string.Empty;
            MemoryStatusText.Text = $"记忆写入成功：{id}";
            await RefreshMemoriesAsync();
        }
        catch (Exception ex)
        {
            MemoryStatusText.Text = $"写入失败：{ex.Message}";
        }
    }

    private async Task RefreshToolingStatusAsync()
    {
        if (!await IsClientOnlineAsync())
        {
            CursorStateText.Text = "Cursor: Client 未运行";
            CodexStateText.Text = "Codex: Client 未运行";
            ToolingStatusText.Text = "工具状态不可用（请先启动 Client）。";
            return;
        }

        try
        {
            var tooling = await GetJsonAsync("http://127.0.0.1:5052/api/client/tooling/list");
            var cursorInstalled = false;
            var codexInstalled = false;

            if (tooling.TryGetProperty("targets", out var targets) && targets.ValueKind == JsonValueKind.Array)
            {
                foreach (var target in targets.EnumerateArray())
                {
                    var id = GetString(target, "id", string.Empty) ?? string.Empty;
                    var installed = target.TryGetProperty("installed", out var installedElement) &&
                                    installedElement.ValueKind == JsonValueKind.True;

                    if (string.Equals(id, "cursor", StringComparison.OrdinalIgnoreCase))
                        cursorInstalled = installed;
                    if (string.Equals(id, "codex", StringComparison.OrdinalIgnoreCase))
                        codexInstalled = installed;
                }
            }

            CursorStateText.Text = cursorInstalled ? "Cursor: 已安装" : "Cursor: 未安装";
            CodexStateText.Text = codexInstalled ? "Codex: 已安装" : "Codex: 未安装";
            ToolingStatusText.Text = $"工具状态已刷新。工作区：{GetString(tooling, "workspaceRoot", "-")}";
        }
        catch (Exception ex)
        {
            CursorStateText.Text = "Cursor: 错误";
            CodexStateText.Text = "Codex: 错误";
            ToolingStatusText.Text = $"工具状态刷新失败：{ex.Message}";
        }
    }

    private async Task InstallToolingAsync(string target)
    {
        if (!await IsClientOnlineAsync())
        {
            ToolingStatusText.Text = "Client 未运行，请先启动 Client。";
            return;
        }

        var project = EnsureProjectSelected();

        try
        {
            ToolingStatusText.Text = $"正在选择 {FormatToolingTargetName(target)} 安装目录...";
            var picked = await PostJsonAsync("http://127.0.0.1:5052/api/client/tooling/select-folder", new
            {
                defaultWorkspaceRoot = project.ProjectRoot,
                prompt = $"选择要安装 {FormatToolingTargetName(target)} 工作流配置的项目目录"
            });

            var accepted = picked.TryGetProperty("selected", out var selectedElement) &&
                           selectedElement.ValueKind == JsonValueKind.True;
            if (!accepted)
            {
                ToolingStatusText.Text = "已取消目录选择。";
                return;
            }

            var workspaceRoot = GetString(picked, "workspaceRoot", string.Empty);
            if (string.IsNullOrWhiteSpace(workspaceRoot))
                throw new InvalidOperationException("未获取到有效工作区目录。");

            var install = await PostJsonAsync("http://127.0.0.1:5052/api/client/tooling/install", new
            {
                target,
                replaceExisting = true,
                workspaceRoot
            });

            var reportsCount = install.TryGetProperty("reports", out var reports) && reports.ValueKind == JsonValueKind.Array
                ? reports.GetArrayLength()
                : 0;

            ToolingStatusText.Text = $"{FormatToolingTargetName(target)} 安装完成（报告 {reportsCount} 条），目录：{workspaceRoot}";
            await RefreshToolingStatusAsync();
        }
        catch (Exception ex)
        {
            ToolingStatusText.Text = $"安装失败：{ex.Message}";
        }
    }

    private async Task RefreshMcpToolsAsync()
    {
        if (!await IsClientOnlineAsync())
        {
            McpSummaryText.Text = "MCP 清单不可用（请先启动 Client）。";
            McpToolListBox.ItemsSource = Array.Empty<string>();
            return;
        }

        try
        {
            var catalog = await GetJsonAsync("http://127.0.0.1:5052/api/client/mcp/tools");
            var endpoint = GetString(catalog, "mcpEndpoint", GetString(catalog, "endpoint", "http://127.0.0.1:5052/mcp"));

            var lines = new List<string>();
            if (catalog.TryGetProperty("tools", out var tools) && tools.ValueKind == JsonValueKind.Array)
            {
                foreach (var tool in tools.EnumerateArray())
                {
                    var name = GetString(tool, "name", "-");
                    var group = GetString(tool, "group", "General");
                    var description = GetString(tool, "description", string.Empty);
                    lines.Add($"[{group}] {name} - {description}");
                }
            }

            McpSummaryText.Text = $"MCP 入口：{endpoint}，工具数：{lines.Count}";
            McpToolListBox.ItemsSource = lines;
        }
        catch (Exception ex)
        {
            McpSummaryText.Text = $"MCP 清单加载失败：{ex.Message}";
            McpToolListBox.ItemsSource = Array.Empty<string>();
        }
    }

    private async Task<string?> PickProjectFolderAsync()
    {
        if (StorageProvider is not { CanOpen: true })
            throw new InvalidOperationException("当前平台不支持文件夹选择器。");

        var startLocation = string.IsNullOrWhiteSpace(_project?.ProjectRoot)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : _project.ProjectRoot;

        IStorageFolder? suggested = null;
        if (Directory.Exists(startLocation))
            suggested = await StorageProvider.TryGetFolderFromPathAsync(startLocation);

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择目标项目根目录（必须包含 .project.dna）",
            AllowMultiple = false,
            SuggestedStartLocation = suggested
        });

        if (folders.Count == 0)
            return null;

        return folders[0].Path.LocalPath;
    }

    private void ApplyProject(DesktopProjectConfig project)
    {
        _project = project;

        ProjectRootBox.Text = project.ProjectRoot;
        ProjectNameBox.Text = project.ProjectName;
        ServerBaseUrlBox.Text = project.ServerBaseUrl;
        ProjectConfigPathText.Text = $"配置文件: {project.ConfigPath}";
        SelectedProjectText.Text = $"目标项目: {project.ProjectName}";
    }

    private DesktopProjectConfig EnsureProjectSelected()
    {
        return _project ?? throw new InvalidOperationException("请先选择目标项目并完成 .project.dna 校验。");
    }

    private static async Task<bool> IsClientOnlineAsync()
    {
        try
        {
            using var response = await StatusHttp.GetAsync("http://127.0.0.1:5052/api/client/status");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> IsServerOnlineAsync(string serverBaseUrl)
    {
        try
        {
            using var response = await StatusHttp.GetAsync($"{serverBaseUrl.TrimEnd('/')}/api/status");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private async Task<JsonElement> GetJsonAsync(string absoluteUrl)
    {
        using var response = await ApiHttp.GetAsync(absoluteUrl);
        return await ReadJsonAsync(response, absoluteUrl);
    }

    private async Task<JsonElement> PostJsonAsync(string absoluteUrl, object payload)
    {
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await ApiHttp.PostAsync(absoluteUrl, content);
        return await ReadJsonAsync(response, absoluteUrl);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response, string requestUrl)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            var reason = string.IsNullOrWhiteSpace(body) ? response.ReasonPhrase : body;
            throw new InvalidOperationException($"{requestUrl} -> {(int)response.StatusCode} {reason}");
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            using var empty = JsonDocument.Parse("{}");
            return empty.RootElement.Clone();
        }

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.Clone();
    }

    private static string? GetString(JsonElement element, string propertyName, string? fallback = null)
    {
        if (!element.TryGetProperty(propertyName, out var value))
            return fallback;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => fallback
        };
    }

    private static DateTime ParseDate(JsonElement element, string propertyName)
    {
        var raw = GetString(element, propertyName, null);
        if (string.IsNullOrWhiteSpace(raw))
            return DateTime.MinValue;

        return DateTime.TryParse(raw, out var parsed)
            ? parsed
            : DateTime.MinValue;
    }

    private static string BuildMemoryLine(JsonElement memory)
    {
        var createdAt = ParseDate(memory, "createdAt");
        var time = createdAt == DateTime.MinValue ? "--" : createdAt.ToLocalTime().ToString("MM-dd HH:mm");
        var type = GetMemoryTypeLabel(memory.TryGetProperty("type", out var typeValue) ? typeValue.ToString() : "-");
        var summary = GetString(memory, "summary", null);
        if (string.IsNullOrWhiteSpace(summary))
            summary = GetString(memory, "content", "(空内容)");

        if (!string.IsNullOrWhiteSpace(summary) && summary.Length > 120)
            summary = summary[..120] + "...";

        return $"{time} | {type} | {summary}";
    }

    private static string GetMemoryTypeLabel(string raw)
    {
        return raw switch
        {
            "0" or "Structural" => "Structural",
            "1" or "Semantic" => "Semantic",
            "2" or "Episodic" => "Episodic",
            "3" or "Working" => "Working",
            "4" or "Procedural" => "Procedural",
            _ => raw
        };
    }

    private int ResolveMemoryTypeValue()
    {
        var selected = (MemoryTypeBox.SelectedItem as ComboBoxItem)?.Content?.ToString()?.Trim();
        return selected switch
        {
            "Structural" => 0,
            "Semantic" => 1,
            "Episodic" => 2,
            "Working" => 3,
            "Procedural" => 4,
            _ => 2
        };
    }

    private static List<string> ParseTags(string? raw)
    {
        var tags = (raw ?? string.Empty)
            .Split([',', ';', '\n', '\r', '\t', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(tag => tag.StartsWith('#') ? tag : $"#{tag}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (tags.Count == 0)
            tags.Add("#desktop-note");

        return tags;
    }

    private static string FormatToolingTargetName(string target)
        => string.Equals(target, "cursor", StringComparison.OrdinalIgnoreCase) ? "Cursor" : "Codex";
}
