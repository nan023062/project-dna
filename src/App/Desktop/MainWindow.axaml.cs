using System.ComponentModel;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Dna.App.Desktop.Services;
using Dna.App.Desktop.ViewModels;
using Dna.App.Services;
using Dna.Core.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace Dna.App.Desktop;

public partial class MainWindow : Window
{
    private const string DepartmentNodePrefix = "__dept__:";
    private const string LocalRuntimeModeLabel = "single-process-local-runtime";
    private readonly EmbeddedAppHost _host;
    private readonly IDnaApiClient _apiClient;
    private readonly DispatcherTimer _statusTimer;
    private readonly DesktopRecentProjectsStore _recentProjectsStore;

    private DesktopProjectConfig? _project;
    private List<DesktopRecentProjectEntry> _recentProjects = [];
    private ConnectionAccessState _connectionAccess = ConnectionAccessState.None;

    public MainWindowViewModel ViewModel { get; }

    public MainWindow()
        : this(new EmbeddedAppHost())
    {
    }

    public MainWindow(EmbeddedAppHost host)
    {
        _host = host;
        _apiClient = App.Current?.Services?.GetService<IDnaApiClient>() ?? new DnaApiClient();
        ViewModel = new MainWindowViewModel(_apiClient);
        DataContext = ViewModel;
        _recentProjectsStore = DesktopRecentProjectsStore.CreateDefault();

        ViewModel.PickProjectFolderAsync = PickProjectFolderAsync;
        ViewModel.LoadProjectAsyncHandler = LoadProjectAsync;
        ViewModel.LoadRecentProjectsHandler = () =>
        {
            _recentProjects = _recentProjectsStore.Load().ToList();
            return _recentProjects;
        };
        ViewModel.RemoveRecentProjectHandler = projectRoot => _recentProjectsStore.Remove(projectRoot);
        ViewModel.CloseWindowHandler = Close;

        InitializeComponent();
        InitializeDefaults();
        ViewModel.WorkspaceExplorer.Reset();
        ViewModel.Topology.Reset();

        ViewModel.Topology.GraphChanged += RefreshTopologyGraph;
        ViewModel.Topology.NavigateRootRequested += HandleTopologyNavigateRootRequested;
        ViewModel.Topology.CopyMcpRequested += HandleTopologyCopyMcpRequested;
        ViewModel.Topology.PropertyChanged += Topology_PropertyChanged;

        TopologyGraph.NodeSelected += HandleTopologyNodeSelected;
        TopologyGraph.NodeInvoked += HandleTopologyNodeInvoked;
        TopologyGraph.ScopeChanged += HandleTopologyScopeChanged;

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _statusTimer.Tick += async (_, _) => await RefreshRuntimeStatusAsync();
        _statusTimer.Start();

        Opened += async (_, _) => await OnOpenedAsync();
        Closed += (_, _) =>
        {
            _statusTimer.Stop();
            ViewModel.Chat.StopChat();
        };
    }

    private static string BuildRuntimeEndpointSummary()
        => $"App API: {AppRuntimeConstants.ApiBaseUrl}/api/*\n" +
           $"Desktop API: {AppRuntimeConstants.ApiBaseUrl}/api/app/*\n" +
           $"MCP: {AppRuntimeConstants.ApiBaseUrl}/mcp\n" +
           $"CLI: agentic-os cli";

    private async Task OnOpenedAsync()
    {
        RefreshRecentProjects();
        ShowProjectLoadPage("请选择项目。加载后会进入工作区。");
        await RefreshRuntimeStatusAsync();
    }

    private void RefreshTopologyGraph()
    {
        TopologyGraph.SetTopology(ViewModel.Topology.GraphNodes.ToList(), ViewModel.Topology.GraphEdges.ToList());
        ApplyTopologyFilters();
        SyncTopologyScope();
    }

    private void ApplyTopologyFilters()
    {
        TopologyGraph.SetRelationFilter(
            ViewModel.Topology.ShowDependency,
            ViewModel.Topology.ShowComposition,
            ViewModel.Topology.ShowAggregation,
            ViewModel.Topology.ShowParentChild,
            ViewModel.Topology.ShowCollaboration);
    }

    private void SyncTopologyScope()
    {
        ViewModel.Topology.ApplyScope(
            TopologyGraph.ViewRootId,
            TopologyGraph.ScopeTrailIds,
            TopologyGraph.VisibleNodeIds,
            TopologyGraph.VisibleEdges);
    }

    private void Topology_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TopologyViewModel.ShowDependency) or
            nameof(TopologyViewModel.ShowComposition) or
            nameof(TopologyViewModel.ShowAggregation) or
            nameof(TopologyViewModel.ShowParentChild) or
            nameof(TopologyViewModel.ShowCollaboration))
        {
            ApplyTopologyFilters();
            SyncTopologyScope();
        }
    }

    private async void HandleTopologyNodeSelected(TopologyNodeViewModel node)
    {
        await ViewModel.Topology.SelectNodeAsync(node.NodeId);
    }

    private void HandleTopologyNodeInvoked(TopologyNodeViewModel node)
    {
        if (TopologyGraph.NavigateInto(node.Id))
            SyncTopologyScope();
    }

    private void HandleTopologyScopeChanged()
    {
        SyncTopologyScope();
    }

    private void HandleTopologyNavigateRootRequested()
    {
        TopologyGraph.NavigateRoot();
        SyncTopologyScope();
    }

    private async void HandleTopologyCopyMcpRequested(string text)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
            await clipboard.SetTextAsync(text);
    }



    private Dictionary<string, string> BuildTopologyMetadataDictionary()
    {
        var metadata = ParseMetadataJson(TopologyEditMetadataBox.Text)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        UpsertMetadataList(metadata, "workflow", ParseMultilineList(TopologyEditWorkflowBox.Text));
        UpsertMetadataList(metadata, "rules", ParseMultilineList(TopologyEditRulesBox.Text));
        UpsertMetadataList(metadata, "prohibitions", ParseMultilineList(TopologyEditProhibitionsBox.Text));

        return metadata
            .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);
    }


    private static string NormalizeInlineText(string? raw)
        => (raw ?? string.Empty).Trim();

    private static string NormalizeMultilineText(string? raw)
        => (raw ?? string.Empty).Replace("\r\n", "\n").Trim();

    private static string ToMcpStringLiteral(string value)
        => JsonSerializer.Serialize(value);

    private static string ToMcpNullableStringLiteral(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? "null"
            : JsonSerializer.Serialize(value.Trim());

    private static string ToMcpNullableCsvLiteral(IEnumerable<string> values)
    {
        var normalized = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return normalized.Count == 0
            ? "null"
            : JsonSerializer.Serialize(string.Join(",", normalized));
    }

    private void InitializeDefaults()
    {
        ViewModel.Memory.Reset();
        ViewModel.Tooling.Reset();
        ViewModel.Chat.Reset();
        ViewModel.RefreshRecentProjects();
        ViewModel.ShowProjectLoadPage("请选择项目。");
    }

    private async void RecentProjectsListBox_OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        await ViewModel.LoadSelectedRecentCommand.ExecuteAsync(null);
    }

    private async Task LoadProjectAsync(string projectRoot)
    {
        try
        {
            if (_project is not null &&
                !string.Equals(_project.ProjectRoot, projectRoot, StringComparison.OrdinalIgnoreCase))
            {
                ViewModel.Chat.StopChat();
            }

            var project = DesktopProjectConfig.Load(projectRoot);
            project.EnsureProjectScopedAppState();
            AppDesktopLog.ConfigureProject(project);
            AppDesktopLog.CreateLogger<MainWindow>().LogInformation(
                LogEvents.Workspace,
                "Loading desktop workspace: project={ProjectName}, root={ProjectRoot}, runtime={RuntimeBaseUrl}, logDir={LogDirectory}",
                project.ProjectName,
                project.ProjectRoot,
                AppRuntimeConstants.ApiBaseUrl,
                project.LogDirectoryPath);

            await EnsureAppRunningAsync(project);
            ApplyProject(project);

            _recentProjectsStore.Upsert(project);
            RefreshRecentProjects(project.ProjectRoot);

            ShowWorkspacePage();
            ViewModel.StatusText = $"已加载工作区：{project.ProjectName}";
            ViewModel.LoaderStatusText = $"已加载：{project.ProjectName}";

            await RefreshAllAsync();
        }
        catch (Exception ex)
        {
            ViewModel.LoaderStatusText = $"加载失败：{ex.Message}";
            if (ViewModel.IsWorkspaceVisible)
                ViewModel.StatusText = $"切换工作区失败：{ex.Message}";
        }
    }

    private async Task EnsureAppRunningAsync(DesktopProjectConfig project)
    {
        if (_host.IsRunning && IsSameProject(_host.CurrentProject, project))
            return;

        if (_host.IsRunning)
            await _host.StopAsync();

        await _host.StartAsync(project);
    }

    private static bool IsSameProject(DesktopProjectConfig? a, DesktopProjectConfig? b)
    {
        return a is not null &&
               b is not null &&
               string.Equals(a.ProjectRoot, b.ProjectRoot, StringComparison.OrdinalIgnoreCase);
    }

    private void ShowProjectLoadPage(string status)
    {
        ViewModel.ShowProjectLoadPage(status);
    }

    private void ShowWorkspacePage()
    {
        ViewModel.ShowWorkspacePage();
    }

    private void UpdateWindowTitle()
    {
        ViewModel.UpdateWindowTitle();
    }

    private void RefreshRecentProjects(string? preferredProjectRoot = null)
    {
        _recentProjects = _recentProjectsStore.Load().ToList();
        ViewModel.RefreshRecentProjects(preferredProjectRoot);
    }

    private async Task RefreshAllAsync()
    {
        await RefreshRuntimeStatusAsync();
        await ViewModel.WorkspaceExplorer.RefreshAsync(_project?.ProjectRoot ?? string.Empty);
        await ViewModel.Topology.RefreshAsync(_project?.ProjectRoot);
        await ViewModel.Memory.RefreshMemoriesAsync(_project?.ProjectRoot);
        await ViewModel.Tooling.RefreshToolingStatusAsync(_project?.ProjectRoot);
        await ViewModel.Tooling.RefreshMcpToolsAsync();
        await ViewModel.Chat.RefreshChatShellAsync(_project?.ProjectRoot);
    }


    private async Task RefreshRuntimeStatusAsync()
    {
        var appOnline = await IsAppOnlineAsync();
        ViewModel.AppStateText = appOnline
            ? $"App: 在线（{AppRuntimeConstants.ApiBaseUrl}）"
            : "App: 离线";

        if (_project is null)
        {
            ViewModel.SelectedProjectText = "目标项目: 未选择";
            ViewModel.ServerStateText = "本地知识库: 未选择项目";
            _connectionAccess = ConnectionAccessState.None;
            ApplyConnectionState(appOnline);
            await RefreshLogTailAsync();
            return;
        }

        ViewModel.SelectedProjectText = $"目标项目: {_project.ProjectName}";

        try
        {
            if (!appOnline)
            {
                ViewModel.ServerStateText = "本地知识库: 运行时离线";
                _connectionAccess = ConnectionAccessState.RuntimeOffline(AppRuntimeConstants.ApiBaseUrl);
                ApplyConnectionState(appOnline);
                await RefreshLogTailAsync();
                return;
            }

            var runtime = await _apiClient.GetAsync("/api/status");
            var moduleCount = ParseNullableInt(runtime, "moduleCount") ?? 0;
            var memoryCount = ParseNullableInt(runtime, "memoryCount") ?? 0;
            ViewModel.ServerStateText = $"本地知识库: {moduleCount} 模块 / {memoryCount} 记忆";

            var access = await _apiClient.GetAsync("/api/connection/access");
            var allowed = access.TryGetProperty("allowed", out var allowedElement) &&
                          allowedElement.ValueKind == JsonValueKind.True;

            _connectionAccess = new ConnectionAccessState(
                HasProject: true,
                RuntimeOnline: true,
                Allowed: allowed,
                Role: GetString(access, "role", "unknown") ?? "unknown",
                EntryName: GetString(access, "entryName", "-") ?? "-",
                RemoteIp: GetString(access, "remoteIp", "-") ?? "-",
                Note: GetString(access, "note", null),
                Reason: GetString(access, "reason", allowed ? string.Empty : "当前本地运行时未授权") ?? string.Empty);
        }
        catch (Exception ex)
        {
            ViewModel.ServerStateText = "本地知识库: 状态读取失败";
            _connectionAccess = ConnectionAccessState.Failed(AppRuntimeConstants.ApiBaseUrl, ex.Message);
        }

        ApplyConnectionState(appOnline);
        await RefreshLogTailAsync();
    }


    private static string ToMultilineText(IEnumerable<string>? values)
    {
        return values is null
            ? string.Empty
            : string.Join(Environment.NewLine, values.Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    private static List<string> ParseMultilineList(string? raw)
    {
        return (raw ?? string.Empty)
            .Split(['\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static Dictionary<string, string>? ParseMetadataJson(string? raw)
    {
        var text = string.IsNullOrWhiteSpace(raw) ? "{}" : raw.Trim();
        var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(text);
        return parsed is { Count: > 0 }
            ? new Dictionary<string, string>(parsed, StringComparer.OrdinalIgnoreCase)
            : null;
    }

    private static void UpsertMetadataList(Dictionary<string, string>? metadata, string key, List<string> values)
    {
        if (metadata is null)
            return;

        if (values.Count == 0)
        {
            metadata.Remove(key);
            return;
        }

        metadata[key] = JsonSerializer.Serialize(values);
    }

    private static string? EmptyToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }



    private static IReadOnlyList<string> ParseStringArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();

        return value.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyDictionary<string, string> ParseStringDictionary(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Object)
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in value.EnumerateObject())
        {
            var raw = property.Value.ValueKind == JsonValueKind.String
                ? property.Value.GetString()
                : property.Value.ToString();
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            result[property.Name] = raw.Trim();
        }

        return result;
    }

    private async Task<string?> PickProjectFolderAsync()
    {
        if (StorageProvider is not { CanOpen: true })
            throw new InvalidOperationException("当前平台不支持文件夹选择器。");

        var selectedRecentRoot = ViewModel.SelectedRecentProject?.ProjectRoot;
        var startLocation = !string.IsNullOrWhiteSpace(selectedRecentRoot)
            ? selectedRecentRoot
            : string.IsNullOrWhiteSpace(_project?.ProjectRoot)
                ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                : _project.ProjectRoot;

        IStorageFolder? suggested = null;
        if (!string.IsNullOrWhiteSpace(startLocation) && Directory.Exists(startLocation))
            suggested = await StorageProvider.TryGetFolderFromPathAsync(startLocation);

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择目标项目根目录（.agentic-os 将在需要时自动创建）",
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
        ViewModel.Chat.ApplyProject(project);
        ViewModel.ProjectName = project.ProjectName;
        ViewModel.ProjectRoot = project.ProjectRoot;

        ViewModel.SelectedProjectText = $"目标项目: {project.ProjectName}";
        ViewModel.LaunchModeText = $"启动方式: 桌面主宿主 + 嵌入式 App API | MCP: {AppRuntimeConstants.ApiBaseUrl}/mcp | CLI: agentic-os cli";
        ViewModel.OverviewModeText = BuildWorkspaceModeText(project);
        ViewModel.Tooling.ToolingHubText = $"当前项目 {project.ProjectName} 已连接本地 5052 运行时，可为 IDE Agent 暴露 API、CLI 与 /mcp。";
        ViewModel.ConnectionServerText = BuildRuntimeEndpointSummary();

        UpdateWindowTitle();
    }

    private DesktopProjectConfig EnsureProjectSelected()
    {
        return _project ?? throw new InvalidOperationException("请先选择目标项目目录。");
    }

    private async Task<bool> IsAppOnlineAsync()
    {
        try
        {
            await _apiClient.GetAsync("/api/app/status");
            return true;
        }
        catch
        {
            return false;
        }
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

    private static int? ParseNullableInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
            return null;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            return number;

        if (value.ValueKind == JsonValueKind.String &&
            int.TryParse(value.GetString(), out var parsed))
        {
            return parsed;
        }

        return null;
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

    private void ApplyConnectionState(bool appOnline)
    {
        if (_project is null || !_connectionAccess.HasProject)
        {
            ViewModel.AccessStateText = "写入边界: 未选择项目";
            ViewModel.ConnectionStatusText = "等待项目加载";
            ViewModel.ConnectionRoleText = "模式: -";
            ViewModel.ConnectionIpText = "来源: -";
            ViewModel.ConnectionServerText = "尚未选择项目。";
            ViewModel.ConnectionNoteText = "本地状态页会集中显示 5052 运行时、当前项目与写入边界。";
            ViewModel.ConnectionRecommendationText = "加载项目后自动读取本地 /api/status 与 /api/connection/access。";
            ViewModel.Memory.ApplyConnectionState(_connectionAccess);
            ViewModel.Chat.ApplyConnectionState(_connectionAccess);
            ViewModel.StatusText = "请先在项目加载页选择项目，进入工作区后会自动拉起本地 5052 运行时。";
            ViewModel.OverviewPrimaryActionText = "先加载一个项目目录；如缺少 .agentic-os，桌面宿主会自动创建并初始化运行数据。";
            ViewModel.OverviewWorkflowText = "推荐顺序：概览 -> 本地状态 -> 知识图谱/记忆 -> 连接Agent。";
            ViewModel.OverviewRecommendationText = "当前以单人本地管理员 MVP 为主，优先把桌面主路径稳定下来。";
            
            return;
        }

        ViewModel.ConnectionServerText = BuildRuntimeEndpointSummary();

        if (!_connectionAccess.RuntimeOnline)
        {
            ViewModel.AccessStateText = "写入边界: 运行时离线";
            ViewModel.ConnectionStatusText = "本地运行时离线";
            ViewModel.ConnectionRoleText = "模式: -";
            ViewModel.ConnectionIpText = "来源: -";
            ViewModel.ConnectionNoteText = "当前无法从本地 5052 运行时读取项目状态。";
            ViewModel.ConnectionRecommendationText = "请先恢复桌面宿主或释放 5052 端口，再重新进入项目。";
            ViewModel.Memory.ApplyConnectionState(_connectionAccess);
            ViewModel.Chat.ApplyConnectionState(_connectionAccess);
            ViewModel.StatusText = "本地运行时尚未连接成功，建议先恢复桌面宿主再继续。";
            ViewModel.OverviewPrimaryActionText = appOnline
                ? "桌面宿主仍在运行，但本地知识库状态未同步；下一步请刷新当前项目。"
                : "请先确认桌面宿主已经启动，并且 5052 没有被其他进程占用。";
            ViewModel.OverviewWorkflowText = "运行时恢复后，重新打开“本地状态”确认项目与写入边界。";
            ViewModel.OverviewRecommendationText = "单机 MVP 优先保证“项目加载 -> 本地 5052 -> 图谱/记忆/MCP”这条主路径稳定。";
            
            return;
        }

        if (!_connectionAccess.Allowed)
        {
            var reason = string.IsNullOrWhiteSpace(_connectionAccess.Reason) ? "当前本地运行时未开放写入" : _connectionAccess.Reason;
            ViewModel.AccessStateText = $"写入边界: 只读（{reason}）";
            ViewModel.ConnectionStatusText = "本地运行时已连接，但当前模式只读";
            ViewModel.ConnectionRoleText = "模式: 只读";
            ViewModel.ConnectionIpText = $"来源: {_connectionAccess.RemoteIp}";
            ViewModel.ConnectionNoteText = $"运行时说明：{reason}";
            ViewModel.ConnectionRecommendationText = "请确认当前项目是否完整加载，并检查本地运行时的写入策略。";
            ViewModel.Memory.ApplyConnectionState(_connectionAccess);
            ViewModel.Chat.ApplyConnectionState(_connectionAccess);
            ViewModel.StatusText = "本地运行时已经连接，但当前模式不允许写入。";
            ViewModel.OverviewPrimaryActionText = "下一步建议先检查项目配置与写入边界，再继续修改知识。";
            ViewModel.OverviewWorkflowText = "只读模式下可继续浏览图谱、记忆与 MCP 工具。";
            ViewModel.OverviewRecommendationText = "单机 MVP 默认推荐以本地管理员模式运行，避免手动编辑与 MCP 写入出现分叉。";
            
            return;
        }

        var roleLabel = DescribeRole(_connectionAccess.Role);
        ViewModel.AccessStateText = $"写入边界: {roleLabel}";
        ViewModel.ConnectionStatusText = "本地运行时已连接";
        ViewModel.ConnectionRoleText = $"模式: {roleLabel}";
        ViewModel.ConnectionIpText = $"来源: {_connectionAccess.EntryName} | {_connectionAccess.RemoteIp}";
        ViewModel.ConnectionNoteText = string.IsNullOrWhiteSpace(_connectionAccess.Note)
            ? $"运行时来源：{_connectionAccess.EntryName}"
            : $"运行时来源：{_connectionAccess.EntryName} | 备注：{_connectionAccess.Note}";

        if (_connectionAccess.IsAdmin)
        {
            ViewModel.ConnectionRecommendationText = "当前为本地管理员模式，可直接维护项目知识图谱、记忆，并继续使用连接Agent能力。";
            ViewModel.Memory.ApplyConnectionState(_connectionAccess);
            ViewModel.Chat.ApplyConnectionState(_connectionAccess);
            ViewModel.StatusText = "主路径已打通，可以继续浏览知识图谱、查看记忆并写入本地知识。";
            ViewModel.OverviewPrimaryActionText = "当前已具备本地管理员能力，建议优先检查图谱、记忆和 Agent 连接配置是否完整。";
            ViewModel.OverviewWorkflowText = "推荐顺序：本地状态确认 -> 知识图谱预览 -> 记忆维护 -> 连接Agent分发到 IDE。";
            ViewModel.OverviewRecommendationText = "如果只是单人本地 MVP，这已经是最完整的闭环路径。";
            return;
        }

        ViewModel.ConnectionRecommendationText = "当前不是本地管理员模式，可浏览本地知识并使用 CLI / MCP；写入入口保持关闭。";
        ViewModel.Memory.ApplyConnectionState(_connectionAccess);
        ViewModel.Chat.ApplyConnectionState(_connectionAccess);
        ViewModel.StatusText = "当前可浏览本地知识并使用 CLI / MCP，本地写入仍需管理员模式。";
        ViewModel.OverviewPrimaryActionText = "当前连接已可用，下一步建议先浏览知识图谱与记忆，再完成 IDE Agent 连接。";
        ViewModel.OverviewWorkflowText = "后续若重新引入协作流，再把多人审核与权限模型叠加到这条本地主路径上。";
        ViewModel.OverviewRecommendationText = "当前桌面 MVP 先把浏览、编辑、MCP、CLI 这条单机主路径打稳。";
    }

    private static string DescribeRole(string? role)
    {
        return role?.Trim().ToLowerInvariant() switch
        {
            "admin" => "admin（本地可写）",
            "editor" => "editor（本地预留）",
            "viewer" => "viewer（只读浏览）",
            LocalRuntimeModeLabel => "single-process-local-runtime",
            _ => role ?? "unknown"
        };
    }

    private static string BuildWorkspaceModeText(DesktopProjectConfig project)
    {
        _ = project;
        return "当前是单进程桌面主宿主 + 进程内本地运行时模式，知识图谱、记忆、LLM 配置与外置 Agent 接入都围绕当前项目收口。";
    }


    public sealed record ConnectionAccessState(
        bool HasProject,
        bool RuntimeOnline,
        bool Allowed,
        string Role,
        string EntryName,
        string RemoteIp,
        string? Note,
        string Reason)
    {
        public static ConnectionAccessState None { get; } = new(
            HasProject: false,
            RuntimeOnline: false,
            Allowed: false,
            Role: "unknown",
            EntryName: "-",
            RemoteIp: "-",
            Note: null,
            Reason: string.Empty);

        public bool IsAdmin => string.Equals(Role, "admin", StringComparison.OrdinalIgnoreCase);

        public static ConnectionAccessState RuntimeOffline(string baseUrl) => new(
            HasProject: true,
            RuntimeOnline: false,
            Allowed: false,
            Role: "unknown",
            EntryName: "-",
            RemoteIp: "-",
            Note: null,
            Reason: $"runtime offline: {baseUrl}");

        public static ConnectionAccessState Failed(string baseUrl, string message) => new(
            HasProject: true,
            RuntimeOnline: true,
            Allowed: false,
            Role: "unknown",
            EntryName: "-",
            RemoteIp: "-",
            Note: null,
            Reason: $"读取 {baseUrl}/api/connection/access 失败：{message}");
    }
}
