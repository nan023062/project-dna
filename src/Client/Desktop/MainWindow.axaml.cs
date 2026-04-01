using System.Net.Http;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Dna.Client.Services;
using Dna.Core.Logging;

namespace Dna.Client.Desktop;

public partial class MainWindow : Window
{
    private const string DepartmentNodePrefix = "__dept__:";
    private const string LocalRuntimeModeLabel = "single-process-local-runtime";
    private static readonly HttpClient StatusHttp = new() { Timeout = TimeSpan.FromMilliseconds(900) };
    private static readonly HttpClient ApiHttp = new() { Timeout = TimeSpan.FromSeconds(30) };

    private readonly EmbeddedClientHost _host;
    private readonly DispatcherTimer _statusTimer;
    private readonly DesktopRecentProjectsStore _recentProjectsStore;
    private readonly Dictionary<string, TopologyNodeViewModel> _topologyNodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<TopologyEdgeViewModel> _topologyEdges = [];

    private DesktopProjectConfig? _project;
    private string? _selectedTopologyNodeId;
    private string? _topologyEditorNodeId;
    private string _topologyEditorBaselineState = string.Empty;
    private bool _topologyEditorDirty;
    private bool _isPopulatingTopologyEditor;
    private List<DesktopRecentProjectEntry> _recentProjects = [];
    private ConnectionAccessState _connectionAccess = ConnectionAccessState.None;

    public MainWindow()
        : this(new EmbeddedClientHost())
    {
    }

    public MainWindow(EmbeddedClientHost host)
    {
        _host = host;
        _recentProjectsStore = DesktopRecentProjectsStore.CreateDefault();

        InitializeComponent();
        InitializeDefaults();
        HookTopologyEditorEvents();

        TopologyGraph.NodeSelected += TopologyGraph_OnNodeSelected;
        TopologyGraph.NodeInvoked += TopologyGraph_OnNodeInvoked;
        TopologyGraph.ScopeChanged += TopologyGraph_OnScopeChanged;

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _statusTimer.Tick += async (_, _) => await RefreshRuntimeStatusAsync();
        _statusTimer.Start();

        Opened += async (_, _) => await OnOpenedAsync();
        Closed += (_, _) => _statusTimer.Stop();
    }

    private static string BuildLocalUrl(string relativePath)
        => new Uri(new Uri($"{ClientRuntimeConstants.ApiBaseUrl.TrimEnd('/')}/"), relativePath.TrimStart('/')).ToString();

    private static string BuildRuntimeEndpointSummary()
        => $"Client API: {ClientRuntimeConstants.ApiBaseUrl}/api/*\n" +
           $"Desktop API: {ClientRuntimeConstants.ApiBaseUrl}/api/client/*\n" +
           $"MCP: {ClientRuntimeConstants.ApiBaseUrl}/mcp\n" +
           $"CLI: dna_client cli";

    private async Task OnOpenedAsync()
    {
        RefreshRecentProjects();
        ShowProjectLoadPage("请选择项目。加载后会进入工作区。");
        await RefreshRuntimeStatusAsync();
    }

    private void HookTopologyEditorEvents()
    {
        TopologyEditDisciplineBox.SelectionChanged += TopologyEditorSelectionChanged;
        TopologyEditParentBox.SelectionChanged += TopologyEditorSelectionChanged;
        TopologyEditBoundaryBox.SelectionChanged += TopologyEditorSelectionChanged;

        TopologyEditLayerBox.TextChanged += TopologyEditorTextChanged;
        TopologyEditNameBox.TextChanged += TopologyEditorTextChanged;
        TopologyEditPathBox.TextChanged += TopologyEditorTextChanged;
        TopologyEditManagedPathsBox.TextChanged += TopologyEditorTextChanged;
        TopologyEditMaintainerBox.TextChanged += TopologyEditorTextChanged;
        TopologyEditSummaryBox.TextChanged += TopologyEditorTextChanged;
        TopologyEditDependenciesBox.TextChanged += TopologyEditorTextChanged;
        TopologyEditPublicApiBox.TextChanged += TopologyEditorTextChanged;
        TopologyEditConstraintsBox.TextChanged += TopologyEditorTextChanged;
        TopologyEditWorkflowBox.TextChanged += TopologyEditorTextChanged;
        TopologyEditRulesBox.TextChanged += TopologyEditorTextChanged;
        TopologyEditProhibitionsBox.TextChanged += TopologyEditorTextChanged;
        TopologyEditMetadataBox.TextChanged += TopologyEditorTextChanged;
    }

    private void TopologyEditorTextChanged(object? sender, TextChangedEventArgs e)
        => OnTopologyEditorFieldChanged();

    private void TopologyEditorSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isPopulatingTopologyEditor)
            return;

        if (ReferenceEquals(sender, TopologyEditDisciplineBox))
        {
            PopulateTopologyParentOptions(
                (TopologyEditDisciplineBox.SelectedItem as TopologyDisciplineOption)?.Id,
                (TopologyEditParentBox.SelectedItem as TopologyParentOption)?.NodeId,
                _topologyEditorNodeId);
        }

        OnTopologyEditorFieldChanged();
    }

    private void OnTopologyEditorFieldChanged()
    {
        if (_isPopulatingTopologyEditor)
            return;

        UpdateTopologyEditorDirtyState();
        UpdateTopologyMcpPreview();
    }

    private void UpdateTopologyEditorDirtyState()
    {
        if (string.IsNullOrWhiteSpace(_topologyEditorNodeId))
        {
            _topologyEditorDirty = false;
            TopologyEditParallelHintText.Text = "手动保存与 MCP upsert_module 共用同一条模块写入链路。";
            return;
        }

        _topologyEditorDirty = !string.Equals(
            _topologyEditorBaselineState,
            CaptureTopologyEditorState(),
            StringComparison.Ordinal);

        TopologyEditParallelHintText.Text = _topologyEditorDirty
            ? "当前表单有未保存修改；MCP 调用预览已同步到这份草稿。"
            : "手动保存与 MCP upsert_module 共用同一条模块写入链路。";
    }

    private string CaptureTopologyEditorState()
    {
        var state = new
        {
            nodeId = _topologyEditorNodeId ?? string.Empty,
            discipline = (TopologyEditDisciplineBox.SelectedItem as TopologyDisciplineOption)?.Id ?? string.Empty,
            parentModuleId = (TopologyEditParentBox.SelectedItem as TopologyParentOption)?.NodeId ?? string.Empty,
            layer = ResolveTopologyEditorLayerValue(),
            name = NormalizeInlineText(TopologyEditNameBox.Text),
            path = NormalizeInlineText(TopologyEditPathBox.Text),
            managedPaths = ParseMultilineList(TopologyEditManagedPathsBox.Text),
            maintainer = NormalizeInlineText(TopologyEditMaintainerBox.Text),
            summary = NormalizeMultilineText(TopologyEditSummaryBox.Text),
            boundary = GetSelectedBoundary() ?? string.Empty,
            dependencies = ParseMultilineList(TopologyEditDependenciesBox.Text),
            publicApi = ParseMultilineList(TopologyEditPublicApiBox.Text),
            constraints = ParseMultilineList(TopologyEditConstraintsBox.Text),
            workflow = ParseMultilineList(TopologyEditWorkflowBox.Text),
            rules = ParseMultilineList(TopologyEditRulesBox.Text),
            prohibitions = ParseMultilineList(TopologyEditProhibitionsBox.Text),
            metadata = NormalizeMultilineText(TopologyEditMetadataBox.Text)
        };

        return JsonSerializer.Serialize(state);
    }

    private void UpdateTopologyMcpPreview()
    {
        if (string.IsNullOrWhiteSpace(_topologyEditorNodeId))
        {
            TopologyMcpPreviewBox.Text = string.Empty;
            CopyTopologyMcpPreviewButton.IsEnabled = false;
            return;
        }

        try
        {
            TopologyMcpPreviewBox.Text = BuildTopologyMcpPreview();
        }
        catch (Exception ex)
        {
            TopologyMcpPreviewBox.Text = $"// MCP 预览生成失败：{ex.Message}";
        }

        CopyTopologyMcpPreviewButton.IsEnabled = !string.IsNullOrWhiteSpace(TopologyMcpPreviewBox.Text);
    }

    private string BuildTopologyMcpPreview()
    {
        var metadata = BuildTopologyMetadataDictionary();
        var lines = new List<string>
        {
            "upsert_module(",
            $"  id={ToMcpStringLiteral(_topologyEditorNodeId ?? string.Empty)},",
            $"  name={ToMcpStringLiteral((TopologyEditNameBox.Text ?? string.Empty).Trim())},",
            $"  discipline={ToMcpStringLiteral((TopologyEditDisciplineBox.SelectedItem as TopologyDisciplineOption)?.Id ?? string.Empty)},",
            $"  path={ToMcpStringLiteral((TopologyEditPathBox.Text ?? string.Empty).Trim())},",
            $"  layer={ResolveTopologyEditorLayerValue()},",
            $"  parentModuleId={ToMcpNullableStringLiteral((TopologyEditParentBox.SelectedItem as TopologyParentOption)?.NodeId)},",
            $"  managedPaths={ToMcpNullableCsvLiteral(ParseMultilineList(TopologyEditManagedPathsBox.Text))},",
            $"  dependencies={ToMcpNullableCsvLiteral(ParseMultilineList(TopologyEditDependenciesBox.Text))},",
            $"  summary={ToMcpNullableStringLiteral(TopologyEditSummaryBox.Text)},",
            $"  boundary={ToMcpNullableStringLiteral(GetSelectedBoundary())},",
            $"  publicApi={ToMcpNullableCsvLiteral(ParseMultilineList(TopologyEditPublicApiBox.Text))},",
            $"  constraints={ToMcpNullableCsvLiteral(ParseMultilineList(TopologyEditConstraintsBox.Text))},",
            $"  metadata={ToMcpNullableStringLiteral(metadata.Count == 0 ? null : JsonSerializer.Serialize(metadata))},",
            $"  maintainer={ToMcpNullableStringLiteral(TopologyEditMaintainerBox.Text)}",
            ")"
        };

        return string.Join(Environment.NewLine, lines);
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

    private int ResolveTopologyEditorLayerValue()
    {
        return int.TryParse((TopologyEditLayerBox.Text ?? string.Empty).Trim(), out var layer) && layer >= 0
            ? layer
            : 0;
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
        SelectedProjectText.Text = "目标项目: 未选择";
        ClientStateText.Text = "Client: 检查中...";
        ServerStateText.Text = "本地知识库: 检查中...";
        AccessStateText.Text = "写入边界: 检查中...";
        LaunchModeText.Text = "启动方式: 桌面主宿主 + 嵌入式 Client API";
        OverviewModeText.Text = "桌面主宿主负责项目切换、知识图谱预览、记忆维护，以及本地 CLI / MCP 连接。";
        OverviewPrimaryActionText.Text = "先加载一个包含 .project.dna/project.json 的项目，然后确认本地运行时与知识库状态。";
        OverviewWorkflowText.Text = "推荐顺序：概览 -> 本地状态 -> 知识图谱/记忆 -> 连接Agent。";
        OverviewRecommendationText.Text = "当前以单人本地管理员 MVP 为主，优先把桌面主路径稳定下来。";
        StatusText.Text = "请先在项目加载页选择项目，进入工作区后会自动拉起本地 5052 运行时。";

        ConnectionStatusText.Text = "等待项目加载";
        ConnectionRoleText.Text = "模式: -";
        ConnectionIpText.Text = "来源: -";
        ConnectionServerText.Text = "尚未选择项目。";
        ConnectionNoteText.Text = "本地状态页会集中显示 5052 运行时、当前项目与写入边界。";
        ConnectionRecommendationText.Text = "加载项目后自动读取本地 /api/status 与 /api/connection/access。";

        TopologySummaryText.Text = "尚未加载知识图谱。";
        TopologyScopeText.Text = "当前视图：全局根视图";
        TopologyDetailTitle.Text = "未选择节点";
        TopologyDetailMeta.Text = "-";
        TopologyDetailSummary.Text = "点击左侧节点查看节点详情。";
        TopologyRelationListBox.ItemsSource = Array.Empty<string>();
        TopologyListBox.ItemsSource = Array.Empty<TopologyModuleListItem>();
        TopologyEditHintText.Text = "选择真实模块节点后可查看或编辑定义。";
        TopologyEditParallelHintText.Text = "手动保存与 MCP upsert_module 共用同一条模块写入链路。";
        TopologyEditStatusText.Text = "模块编辑区就绪。";
        StatModulesText.Text = "-";
        StatDependenciesText.Text = "-";
        StatCollaborationText.Text = "-";
        StatDisciplinesText.Text = "-";
        ResetTopologyEditor();

        MemoryAccessText.Text = "当前记忆库位于项目 .project.dna；写入能力由本地运行时状态决定。";
        MemoryStatusText.Text = "记忆区就绪。";
        AddMemoryButton.IsEnabled = false;
        ToolingHubText.Text = "Client 保留本地 5052 运行时，既服务桌面宿主内部调用，也向外暴露 CLI 与 /mcp。";
        ToolingStatusText.Text = "工具区就绪。";
        McpSummaryText.Text = "MCP 清单未加载。";

        RecentCountText.Text = "最近项目（0）";
        RecentProjectsListBox.ItemsSource = Array.Empty<RecentProjectListItem>();
        RecentProjectHintText.Text = "尚未选择最近项目。你也可以双击最近项目直接进入，或点击“打开项目目录...”加载新项目。";
        LoaderStatusText.Text = "请选择项目。";

        ProjectLoadPanel.IsVisible = true;
        WorkspacePanel.IsVisible = false;
        ApplyTopologyRelationFilter();
        UpdateWindowTitle();
    }

    private void MenuSwitchWorkspace_OnClick(object? sender, RoutedEventArgs e)
    {
        RefreshRecentProjects();
        ShowProjectLoadPage(_project is null
            ? "请选择项目。"
            : $"当前工作区：{_project.ProjectName}。选择其他项目后会自动切换。");
    }

    private void ReloadRecentProjects_OnClick(object? sender, RoutedEventArgs e)
    {
        RefreshRecentProjects();
        LoaderStatusText.Text = "最近项目列表已刷新。";
    }

    private void MenuExit_OnClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void OpenProjectFolder_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var selected = await PickProjectFolderAsync();
            if (string.IsNullOrWhiteSpace(selected))
            {
                LoaderStatusText.Text = "已取消项目选择。";
                return;
            }

            await LoadProjectAsync(selected);
        }
        catch (Exception ex)
        {
            LoaderStatusText.Text = $"项目选择失败：{ex.Message}";
        }
    }

    private async void LoadSelectedRecent_OnClick(object? sender, RoutedEventArgs e)
    {
        var selected = ResolveSelectedRecent();
        if (selected is null)
        {
            LoaderStatusText.Text = "请先在最近项目列表中选择一项，或直接双击条目加载。";
            return;
        }

        await LoadProjectAsync(selected.ProjectRoot);
    }

    private void RemoveSelectedRecent_OnClick(object? sender, RoutedEventArgs e)
    {
        var selected = ResolveSelectedRecent();
        if (selected is null)
        {
            LoaderStatusText.Text = "请先选择要移除的项目。";
            return;
        }

        _recentProjectsStore.Remove(selected.ProjectRoot);
        RefreshRecentProjects();
        LoaderStatusText.Text = $"已移除：{selected.ProjectName}";
    }

    private void RecentProjectsListBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        RenderRecentProjectHint(ResolveSelectedRecent());
    }

    private async void RecentProjectsListBox_OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        var selected = ResolveSelectedRecent();
        if (selected is null)
        {
            LoaderStatusText.Text = "请先选择一个最近项目。";
            return;
        }

        await LoadProjectAsync(selected.ProjectRoot);
    }

    private void RecentProjectRemoveButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string projectRoot } || string.IsNullOrWhiteSpace(projectRoot))
        {
            LoaderStatusText.Text = "未找到要移除的项目。";
            return;
        }

        var existing = _recentProjects.FirstOrDefault(item =>
            string.Equals(item.ProjectRoot, projectRoot, StringComparison.OrdinalIgnoreCase));
        var name = existing?.ProjectName ?? projectRoot;

        _recentProjectsStore.Remove(projectRoot);
        RefreshRecentProjects();
        LoaderStatusText.Text = $"已移除：{name}";
        e.Handled = true;
    }

    private async Task LoadProjectAsync(string projectRoot)
    {
        try
        {
            var project = DesktopProjectConfig.Load(projectRoot);
            project.EnsureProjectScopedClientState();
            ClientDesktopLog.ConfigureProject(project);
            ClientDesktopLog.CreateLogger<MainWindow>().LogInformation(
                LogEvents.Workspace,
                "Loading desktop workspace: project={ProjectName}, root={ProjectRoot}, runtime={RuntimeBaseUrl}, config={ConfigPath}, logDir={LogDirectory}",
                project.ProjectName,
                project.ProjectRoot,
                ClientRuntimeConstants.ApiBaseUrl,
                project.ConfigPath,
                project.LogDirectoryPath);

            await EnsureClientRunningAsync(project);
            ApplyProject(project);

            _recentProjectsStore.Upsert(project);
            RefreshRecentProjects(project.ProjectRoot);

            ShowWorkspacePage();
            StatusText.Text = $"已加载工作区：{project.ProjectName}";
            LoaderStatusText.Text = $"已加载：{project.ProjectName}";

            await RefreshAllAsync();
        }
        catch (Exception ex)
        {
            LoaderStatusText.Text = $"加载失败：{ex.Message}";
            if (WorkspacePanel.IsVisible)
                StatusText.Text = $"切换工作区失败：{ex.Message}";
        }
    }

    private async Task EnsureClientRunningAsync(DesktopProjectConfig project)
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
        ProjectLoadPanel.IsVisible = true;
        WorkspacePanel.IsVisible = false;
        LoaderStatusText.Text = status;
        UpdateWindowTitle();
    }

    private void ShowWorkspacePage()
    {
        ProjectLoadPanel.IsVisible = false;
        WorkspacePanel.IsVisible = true;
        UpdateWindowTitle();
    }

    private void UpdateWindowTitle()
    {
        if (WorkspacePanel.IsVisible && _project is not null)
        {
            Title = $"Project DNA Client - {_project.ProjectName}";
            return;
        }

        Title = "Project DNA Client - 项目加载";
    }

    private void RefreshRecentProjects(string? preferredProjectRoot = null)
    {
        _recentProjects = _recentProjectsStore.Load().ToList();

        var list = _recentProjects
            .Select(entry => new RecentProjectListItem(entry))
            .ToList();

        RecentProjectsListBox.ItemsSource = list;
        RecentCountText.Text = $"最近项目（{list.Count}）";

        if (list.Count == 0)
        {
            RecentProjectsListBox.SelectedItem = null;
            RenderRecentProjectHint(null);
            return;
        }

        var selected = list[0];
        if (!string.IsNullOrWhiteSpace(preferredProjectRoot))
        {
            selected = list.FirstOrDefault(x =>
                string.Equals(x.Entry.ProjectRoot, preferredProjectRoot, StringComparison.OrdinalIgnoreCase)) ?? selected;
        }

        RecentProjectsListBox.SelectedItem = selected;
        RenderRecentProjectHint(selected.Entry);
    }

    private DesktopRecentProjectEntry? ResolveSelectedRecent()
    {
        return (RecentProjectsListBox.SelectedItem as RecentProjectListItem)?.Entry;
    }

    private void RenderRecentProjectHint(DesktopRecentProjectEntry? entry)
    {
        if (entry is null)
        {
            RecentProjectHintText.Text = "尚未选择最近项目。你也可以双击最近项目直接进入，或点击“打开项目目录...”加载新项目。";
            return;
        }

        var lastOpened = entry.LastOpenedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        RecentProjectHintText.Text =
            $"项目：{entry.ProjectName}\n" +
            $"目录：{entry.ProjectRoot}\n" +
            $"运行时：{ClientRuntimeConstants.ApiBaseUrl}\n" +
            $"上次打开：{lastOpened}";
    }

    private async void RefreshTopology_OnClick(object? sender, RoutedEventArgs e)
    {
        await RefreshTopologyAsync();
    }

    private void TopologyNavigateInto_OnClick(object? sender, RoutedEventArgs e)
    {
        NavigateIntoTopologyNode(_selectedTopologyNodeId, showMessageWhenUnavailable: true);
    }

    private void TopologyNavigateUp_OnClick(object? sender, RoutedEventArgs e)
    {
        NavigateUpTopologyScope(showMessageWhenUnavailable: true);
    }

    private void TopologyNavigateRoot_OnClick(object? sender, RoutedEventArgs e)
    {
        TopologyGraph.NavigateRoot();
    }

    private void TopologyFilter_OnChanged(object? sender, RoutedEventArgs e)
    {
        ApplyTopologyRelationFilter();
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

    private void TopologyGraph_OnNodeSelected(TopologyNodeViewModel node)
    {
        SelectTopologyNode(node.Id, syncGraph: false, syncList: true);
    }

    private void TopologyGraph_OnNodeInvoked(TopologyNodeViewModel node)
    {
        if (string.Equals(node.Id, TopologyGraph.ViewRootId, StringComparison.OrdinalIgnoreCase))
        {
            NavigateUpTopologyScope(showMessageWhenUnavailable: false);
            return;
        }

        NavigateIntoTopologyNode(node.Id, showMessageWhenUnavailable: false);
    }

    private void TopologyGraph_OnScopeChanged()
    {
        UpdateTopologyScopeState(preferredNodeId: _selectedTopologyNodeId);
    }

    private void NavigateIntoTopologyNode(string? nodeId, bool showMessageWhenUnavailable)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
        {
            if (showMessageWhenUnavailable)
                TopologyScopeText.Text = "当前视图：请先选择一个模块节点，再双击进入它的子视图。";
            return;
        }

        if (TopologyGraph.NavigateInto(nodeId))
            return;

        if (!showMessageWhenUnavailable)
            return;

        var label = ResolveNodeLabel(nodeId);
        TopologyScopeText.Text = $"当前视图：{label} 没有可进入的子节点。";
    }

    private void NavigateUpTopologyScope(bool showMessageWhenUnavailable)
    {
        if (TopologyGraph.NavigateUp())
            return;

        if (showMessageWhenUnavailable)
            TopologyScopeText.Text = "当前视图：已经位于全局根视图。";
    }

    private void TopologyListBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (TopologyListBox.SelectedItem is TopologyModuleListItem item)
            SelectTopologyNode(item.Key, syncGraph: true, syncList: false);
    }

    private async Task RefreshAllAsync()
    {
        await RefreshRuntimeStatusAsync();
        await RefreshTopologyAsync();
        await RefreshMemoriesAsync();
        await RefreshToolingStatusAsync();
        await RefreshMcpToolsAsync();
    }

    private async Task RefreshRuntimeStatusAsync()
    {
        var clientOnline = await IsClientOnlineAsync();
        ClientStateText.Text = clientOnline
            ? $"Client: 在线（{ClientRuntimeConstants.ApiBaseUrl}）"
            : "Client: 离线";

        if (_project is null)
        {
            SelectedProjectText.Text = "目标项目: 未选择";
            ServerStateText.Text = "本地知识库: 未选择项目";
            _connectionAccess = ConnectionAccessState.None;
            ApplyConnectionState(clientOnline);
            return;
        }

        SelectedProjectText.Text = $"目标项目: {_project.ProjectName}";

        try
        {
            if (!clientOnline)
            {
                ServerStateText.Text = "本地知识库: 运行时离线";
                _connectionAccess = ConnectionAccessState.RuntimeOffline(ClientRuntimeConstants.ApiBaseUrl);
                ApplyConnectionState(clientOnline);
                return;
            }

            var runtime = await GetJsonAsync(BuildLocalUrl("/api/status"));
            var moduleCount = ParseNullableInt(runtime, "moduleCount") ?? 0;
            var memoryCount = ParseNullableInt(runtime, "memoryCount") ?? 0;
            ServerStateText.Text = $"本地知识库: {moduleCount} 模块 / {memoryCount} 记忆";

            var access = await GetJsonAsync(BuildLocalUrl("/api/connection/access"));
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
            ServerStateText.Text = "本地知识库: 状态读取失败";
            _connectionAccess = ConnectionAccessState.Failed(ClientRuntimeConstants.ApiBaseUrl, ex.Message);
        }

        ApplyConnectionState(clientOnline);
    }

    private async Task RefreshTopologyAsync()
    {
        if (_project is null)
        {
            TopologySummaryText.Text = "未选择项目，无法加载知识图谱。";
            ResetTopologyState();
            return;
        }

        try
        {
            var topology = await GetJsonAsync(BuildLocalUrl("/api/topology"));
            TopologySummaryText.Text = GetString(topology, "summary", "知识图谱已加载")!;

            var nodes = ParseTopologyNodes(topology);
            var edges = ParseTopologyEdges(topology);
            var rawNodeCount = nodes.Count(node => node.CanEdit);

            _topologyNodes.Clear();
            foreach (var node in nodes)
                _topologyNodes[node.Id] = node;

            _topologyEdges.Clear();
            _topologyEdges.AddRange(edges);

            TopologyGraph.SetTopology(nodes, edges);
            ApplyTopologyRelationFilter();

            var dependencyCount = edges.Count(e => string.Equals(e.Relation, "dependency", StringComparison.OrdinalIgnoreCase));
            var collaborationCount = edges.Count(e => string.Equals(e.Relation, "collaboration", StringComparison.OrdinalIgnoreCase));
            var disciplinesCount = topology.TryGetProperty("disciplines", out var disciplines) && disciplines.ValueKind == JsonValueKind.Array
                ? disciplines.GetArrayLength()
                : nodes.Select(n => n.DisciplineLabel).Distinct(StringComparer.OrdinalIgnoreCase).Count();

            StatModulesText.Text = rawNodeCount.ToString();
            StatDependenciesText.Text = dependencyCount.ToString();
            StatCollaborationText.Text = collaborationCount.ToString();
            StatDisciplinesText.Text = disciplinesCount.ToString();
            UpdateTopologyScopeState(preferredNodeId: _selectedTopologyNodeId);
        }
        catch (Exception ex)
        {
            TopologySummaryText.Text = $"知识图谱加载失败：{ex.Message}";
            ResetTopologyState();
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
            var result = await GetJsonAsync(BuildLocalUrl("/api/memory/query?limit=40&offset=0"));
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

        if (!_connectionAccess.IsAdmin)
        {
            MemoryStatusText.Text = "当前运行态未开放写入，本地记忆写入已禁用。";
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

            var saved = await PostJsonAsync(BuildLocalUrl("/api/memory/remember"), payload);
            var id = GetString(saved, "id", "-");

            MemoryContentBox.Text = string.Empty;
            MemoryStatusText.Text = $"本地记忆写入成功：{id}";
            await RefreshMemoriesAsync();
        }
        catch (Exception ex)
        {
            MemoryStatusText.Text = $"写入失败：{ex.Message}";
        }
    }

    private async Task RefreshToolingStatusAsync()
    {
        if (_project is null)
        {
            CursorStateText.Text = "Cursor: 未加载项目";
            CodexStateText.Text = "Codex: 未加载项目";
            ToolingHubText.Text = "请先加载项目工作区，再为 IDE 安装本地 DNA Client 连接配置。";
            ToolingStatusText.Text = "请先加载项目工作区。";
            return;
        }

        if (!await IsClientOnlineAsync())
        {
            CursorStateText.Text = "Cursor: Client 未运行";
            CodexStateText.Text = "Codex: Client 未运行";
            ToolingHubText.Text = "桌面宿主尚未拉起本地 5052 运行时，外部 IDE 暂时无法连接。";
            ToolingStatusText.Text = "工具状态不可用（请先启动 Client）。";
            return;
        }

        try
        {
            var tooling = await GetJsonAsync(BuildLocalUrl("/api/client/tooling/list"));
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

            var workspaceRoot = GetString(tooling, "workspaceRoot", "-");
            CursorStateText.Text = cursorInstalled ? "Cursor: 已安装" : "Cursor: 未安装";
            CodexStateText.Text = codexInstalled ? "Codex: 已安装" : "Codex: 未安装";
            ToolingHubText.Text = $"本地 5052 运行时已就绪。当前工作区：{workspaceRoot}；CLI：dna_client cli；MCP 入口：{ClientRuntimeConstants.ApiBaseUrl}/mcp";
            ToolingStatusText.Text = $"工具状态已刷新。工作区：{workspaceRoot}";
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
            var picked = await PostJsonAsync(BuildLocalUrl("/api/client/tooling/select-folder"), new
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

            var install = await PostJsonAsync(BuildLocalUrl("/api/client/tooling/install"), new
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
            var catalog = await GetJsonAsync(BuildLocalUrl("/api/client/mcp/tools"));
            var endpoint = GetString(catalog, "mcpEndpoint", GetString(catalog, "endpoint", $"{ClientRuntimeConstants.ApiBaseUrl}/mcp"));

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

    private void SelectTopologyNode(string? nodeId, bool syncGraph, bool syncList)
    {
        if (string.IsNullOrWhiteSpace(nodeId) || !_topologyNodes.ContainsKey(nodeId))
        {
            _selectedTopologyNodeId = null;
            if (syncGraph)
                TopologyGraph.SelectNode(null);
            RenderTopologyDetails(null);
            return;
        }

        _selectedTopologyNodeId = nodeId;

        if (syncGraph)
            TopologyGraph.SelectNode(nodeId);

        if (syncList && TopologyListBox.ItemsSource is IEnumerable<TopologyModuleListItem> items)
        {
            var selected = items.FirstOrDefault(i => string.Equals(i.Key, nodeId, StringComparison.OrdinalIgnoreCase));
            if (selected is not null && !ReferenceEquals(TopologyListBox.SelectedItem, selected))
                TopologyListBox.SelectedItem = selected;
        }

        RenderTopologyDetails(nodeId);
    }

    private void UpdateTopologyScopeState(string? preferredNodeId)
    {
        var visibleNodeIds = TopologyGraph.VisibleNodeIds;
        var listItems = visibleNodeIds
            .Select(BuildTopologyListItem)
            .Where(item => item is not null)
            .Cast<TopologyModuleListItem>()
            .ToList();

        TopologyListBox.ItemsSource = listItems;
        TopologyScopeText.Text = BuildTopologyScopeText(visibleNodeIds);

        if (listItems.Count == 0)
        {
            _selectedTopologyNodeId = null;
            TopologyGraph.SelectNode(null);
            RenderTopologyDetails(null);
            return;
        }

        var nextSelectedId = preferredNodeId;
        if (string.IsNullOrWhiteSpace(nextSelectedId) ||
            visibleNodeIds.All(id => !string.Equals(id, nextSelectedId, StringComparison.OrdinalIgnoreCase)))
        {
            nextSelectedId = listItems[0].Key;
        }

        SelectTopologyNode(nextSelectedId, syncGraph: true, syncList: true);
    }

    private TopologyModuleListItem? BuildTopologyListItem(string nodeId)
    {
        if (!_topologyNodes.TryGetValue(nodeId, out var node))
            return null;

        var childCount = TopologyGraph.GetChildCount(nodeId);
        var childHint = childCount > 0 ? $" | children={childCount}" : string.Empty;
        return new TopologyModuleListItem(
            node.Id,
            $"{node.Label} | L{ResolveNodeLayer(node)} | {node.TypeLabel} | {node.DisciplineLabel} | deps={node.DependencyCount}{childHint}");
    }

    private string BuildTopologyScopeText(IReadOnlyList<string> visibleNodeIds)
    {
        var trail = TopologyGraph.ScopeTrailIds.Select(ResolveNodeLabel).ToList();
        if (trail.Count == 0)
        {
            return visibleNodeIds.Count == 0
                ? "当前视图：全局根视图（暂无顶层节点）"
                : $"当前视图：全局根视图（显示顶层节点 {visibleNodeIds.Count} 个，双击节点进入）";
        }

        var path = string.Join(" / ", trail);
        var childCount = visibleNodeIds.Count(id =>
            !string.Equals(id, TopologyGraph.ViewRootId, StringComparison.OrdinalIgnoreCase));

        return childCount == 0
            ? $"当前视图：{path}（中心节点暂无子节点，双击中心节点可返回上一级）"
            : $"当前视图：{path}（中心节点双击返回上一级；当前显示直属子节点 {childCount} 个，双击子节点进入）";
    }

    private void RenderTopologyDetails(string? nodeId)
    {
        if (string.IsNullOrWhiteSpace(nodeId) || !_topologyNodes.TryGetValue(nodeId, out var node))
        {
            TopologyDetailTitle.Text = "未选择节点";
            TopologyDetailMeta.Text = "-";
            TopologyDetailSummary.Text = "单击左侧节点查看详情，双击节点进入子视图；双击中心节点返回上一级。";
            TopologyRelationListBox.ItemsSource = Array.Empty<string>();
            ResetTopologyEditor();
            return;
        }

        var childCount = TopologyGraph.GetChildCount(nodeId);
        var isScopeCenter = !string.IsNullOrWhiteSpace(TopologyGraph.ViewRootId) &&
                            string.Equals(nodeId, TopologyGraph.ViewRootId, StringComparison.OrdinalIgnoreCase);
        TopologyDetailTitle.Text = node.Label;
        TopologyDetailMeta.Text = $"L{ResolveNodeLayer(node)} | {node.TypeLabel} | {node.DisciplineLabel} | deps {node.DependencyCount} | children {childCount}";

        var summary = string.IsNullOrWhiteSpace(node.Summary) ? "暂无摘要。" : node.Summary;
        if (isScopeCenter)
        {
            summary += childCount > 0
                ? "\n\n当前节点是本层中心节点，双击它可返回上一级；周围节点是它的直属子节点。"
                : "\n\n当前节点是本层中心节点，暂无子节点；双击它可返回上一级。";
        }
        else if (childCount > 0)
        {
            summary += "\n\n双击该节点可进入它的子视图。";
        }
        else
        {
            summary += "\n\n该节点暂无可进入的子节点。";
        }

        TopologyDetailSummary.Text = summary;

        var visibleEdges = TopologyGraph.VisibleEdges;
        var outgoing = visibleEdges
            .Where(e =>
                string.Equals(e.From, nodeId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var incoming = visibleEdges
            .Where(e =>
                string.Equals(e.To, nodeId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var lines = new List<string> { $"当前视图内：出边 {outgoing.Count} | 入边 {incoming.Count}" };

        if (outgoing.Count == 0 && incoming.Count == 0)
        {
            lines.Add("当前视图内暂无关联关系。");
        }
        else
        {
            lines.Add(string.Empty);
            lines.AddRange(outgoing
                .OrderBy(e => ResolveTopologyRelationLabel(e), StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => ResolveNodeLabel(e.To), StringComparer.OrdinalIgnoreCase)
                .Select(e => $"OUT [{ResolveTopologyRelationLabel(e)}] -> {ResolveNodeLabel(e.To)}"));

            lines.AddRange(incoming
                .OrderBy(e => ResolveTopologyRelationLabel(e), StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => ResolveNodeLabel(e.From), StringComparer.OrdinalIgnoreCase)
                .Select(e => $"IN  [{ResolveTopologyRelationLabel(e)}] <- {ResolveNodeLabel(e.From)}"));
        }

        TopologyRelationListBox.ItemsSource = lines;
        PopulateTopologyEditor(node);
    }

    private void PopulateTopologyParentOptions(string? disciplineId, string? selectedParentModuleId, string? currentNodeId)
    {
        var parentOptions = _topologyNodes.Values
            .Where(x =>
                x.CanEdit &&
                !string.Equals(x.NodeId, currentNodeId, StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrWhiteSpace(disciplineId) ||
                 string.Equals(x.Discipline, disciplineId, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(x => x.Label, StringComparer.OrdinalIgnoreCase)
            .Select(x => new TopologyParentOption(x.NodeId, x.Label))
            .ToList();

        parentOptions.Insert(0, new TopologyParentOption(null, "(顶层模块)"));

        var previousPopulating = _isPopulatingTopologyEditor;
        _isPopulatingTopologyEditor = true;
        try
        {
            TopologyEditParentBox.ItemsSource = parentOptions;
            TopologyEditParentBox.SelectedItem = parentOptions
                .FirstOrDefault(x => string.Equals(x.NodeId, selectedParentModuleId, StringComparison.OrdinalIgnoreCase))
                ?? parentOptions[0];
        }
        finally
        {
            _isPopulatingTopologyEditor = previousPopulating;
        }
    }

    private void PopulateTopologyEditor(TopologyNodeViewModel node)
    {
        var isEditableModule = node.CanEdit;
        var canSave = isEditableModule && _connectionAccess.IsAdmin;

        TopologyEditHintText.Text = !isEditableModule
            ? "项目根和部门节点由系统生成，不作为业务模块编辑。"
            : canSave
                ? "当前本地运行时允许写入，可直接修改模块定义并保存到项目知识库。"
                : "当前运行态只读，只展示模块定义，不允许保存。";

        var disciplineOptions = _topologyNodes.Values
            .Where(x => string.Equals(x.Type, "Department", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Label, StringComparer.OrdinalIgnoreCase)
            .Select(x => new TopologyDisciplineOption(x.Discipline, x.Label))
            .ToList();
        _isPopulatingTopologyEditor = true;
        try
        {
            _topologyEditorNodeId = isEditableModule ? node.NodeId : null;
            TopologyEditDisciplineBox.ItemsSource = disciplineOptions;
        TopologyEditDisciplineBox.SelectedItem = disciplineOptions
            .FirstOrDefault(x => string.Equals(x.Id, node.Discipline, StringComparison.OrdinalIgnoreCase));

        var parentOptions = _topologyNodes.Values
            .Where(x =>
                x.CanEdit &&
                !string.Equals(x.Id, node.Id, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.Discipline, node.Discipline, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Label, StringComparer.OrdinalIgnoreCase)
            .Select(x => new TopologyParentOption(x.NodeId, x.Label))
            .ToList();
        parentOptions.Insert(0, new TopologyParentOption(null, "(顶层模块)"));
        TopologyEditParentBox.ItemsSource = parentOptions;
        TopologyEditParentBox.SelectedItem = parentOptions
            .FirstOrDefault(x => string.Equals(x.NodeId, node.ParentModuleId, StringComparison.OrdinalIgnoreCase))
            ?? parentOptions[0];
        PopulateTopologyParentOptions(node.Discipline, node.ParentModuleId, node.NodeId);

        TopologyEditLayerBox.Text = (node.ComputedLayer ?? 0).ToString();
        TopologyEditNameBox.Text = node.Label;
        TopologyEditPathBox.Text = node.RelativePath ?? string.Empty;
        TopologyEditManagedPathsBox.Text = ToMultilineText(node.ManagedPathScopes);
        TopologyEditMaintainerBox.Text = node.Maintainer ?? string.Empty;
        TopologyEditSummaryBox.Text = node.Summary ?? string.Empty;
        SetBoundarySelection(node.Boundary);
        TopologyEditDependenciesBox.Text = ToMultilineText(ResolveNodeDependencies(node.Id));
        TopologyEditPublicApiBox.Text = ToMultilineText(node.PublicApi);
        TopologyEditConstraintsBox.Text = ToMultilineText(node.Constraints);
        TopologyEditWorkflowBox.Text = ToMultilineText(node.Workflow);
        TopologyEditRulesBox.Text = ToMultilineText(node.Rules);
        TopologyEditProhibitionsBox.Text = ToMultilineText(node.Prohibitions);
        TopologyEditMetadataBox.Text = node.Metadata is { Count: > 0 }
            ? JsonSerializer.Serialize(node.Metadata, new JsonSerializerOptions { WriteIndented = true })
            : "{}";
        }
        finally
        {
            _isPopulatingTopologyEditor = false;
        }

        SetTopologyEditorEnabled(isEditableModule, canSave);
        _topologyEditorBaselineState = isEditableModule
            ? CaptureTopologyEditorState()
            : string.Empty;
        _topologyEditorDirty = false;
        UpdateTopologyMcpPreview();
        UpdateTopologyEditorDirtyState();
        TopologyEditStatusText.Text = isEditableModule
            ? $"已载入模块：{node.Label}"
            : "当前节点没有可保存的模块定义。";
    }

    private void ResetTopologyEditor()
    {
        _isPopulatingTopologyEditor = true;
        try
        {
        TopologyEditDisciplineBox.ItemsSource = Array.Empty<TopologyDisciplineOption>();
        TopologyEditParentBox.ItemsSource = Array.Empty<TopologyParentOption>();
        TopologyEditDisciplineBox.SelectedItem = null;
        TopologyEditParentBox.SelectedItem = null;
        TopologyEditLayerBox.Text = string.Empty;
        TopologyEditNameBox.Text = string.Empty;
        TopologyEditPathBox.Text = string.Empty;
        TopologyEditManagedPathsBox.Text = string.Empty;
        TopologyEditMaintainerBox.Text = string.Empty;
        TopologyEditSummaryBox.Text = string.Empty;
        SetBoundarySelection(null);
        TopologyEditDependenciesBox.Text = string.Empty;
        TopologyEditPublicApiBox.Text = string.Empty;
        TopologyEditConstraintsBox.Text = string.Empty;
        TopologyEditWorkflowBox.Text = string.Empty;
        TopologyEditRulesBox.Text = string.Empty;
        TopologyEditProhibitionsBox.Text = string.Empty;
        TopologyEditMetadataBox.Text = "{}";
        TopologyMcpPreviewBox.Text = string.Empty;
        }
        finally
        {
            _isPopulatingTopologyEditor = false;
        }

        _topologyEditorNodeId = null;
        _topologyEditorBaselineState = string.Empty;
        _topologyEditorDirty = false;
        SetTopologyEditorEnabled(false, false);
        TopologyEditParallelHintText.Text = "手动保存与 MCP upsert_module 共用同一条模块写入链路。";
    }

    private void SetTopologyEditorEnabled(bool hasModule, bool canSave)
    {
        TopologyEditDisciplineBox.IsEnabled = hasModule && canSave;
        TopologyEditLayerBox.IsEnabled = hasModule && canSave;
        TopologyEditNameBox.IsEnabled = hasModule && canSave;
        TopologyEditPathBox.IsEnabled = hasModule && canSave;
        TopologyEditParentBox.IsEnabled = hasModule && canSave;
        TopologyEditManagedPathsBox.IsEnabled = hasModule && canSave;
        TopologyEditMaintainerBox.IsEnabled = hasModule && canSave;
        TopologyEditSummaryBox.IsEnabled = hasModule && canSave;
        TopologyEditBoundaryBox.IsEnabled = hasModule && canSave;
        TopologyEditDependenciesBox.IsEnabled = hasModule && canSave;
        TopologyEditPublicApiBox.IsEnabled = hasModule && canSave;
        TopologyEditConstraintsBox.IsEnabled = hasModule && canSave;
        TopologyEditWorkflowBox.IsEnabled = hasModule && canSave;
        TopologyEditRulesBox.IsEnabled = hasModule && canSave;
        TopologyEditProhibitionsBox.IsEnabled = hasModule && canSave;
        TopologyEditMetadataBox.IsEnabled = hasModule && canSave;
        TopologyMcpPreviewBox.IsEnabled = hasModule;
        TopologySaveModuleButton.IsEnabled = hasModule && canSave;
        TopologyReloadLatestModuleButton.IsEnabled = hasModule;
        CopyTopologyMcpPreviewButton.IsEnabled = hasModule && !string.IsNullOrWhiteSpace(TopologyMcpPreviewBox.Text);
    }

    private async void SaveTopologyModule_OnClick(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedTopologyNodeId) ||
            !_topologyNodes.TryGetValue(_selectedTopologyNodeId, out var node) ||
            !node.CanEdit)
        {
            TopologyEditStatusText.Text = "请先选择一个真实模块节点。";
            return;
        }

        if (!_connectionAccess.IsAdmin)
        {
            TopologyEditStatusText.Text = "当前角色不是 admin，无法保存模块定义。";
            return;
        }

        if (_project is null)
        {
            TopologyEditStatusText.Text = "未选择项目，无法保存模块定义。";
            return;
        }

        try
        {
            var discipline = (TopologyEditDisciplineBox.SelectedItem as TopologyDisciplineOption)?.Id
                ?? node.Discipline;
            if (string.IsNullOrWhiteSpace(discipline))
                throw new InvalidOperationException("请选择模块所属部门。");

            if (!int.TryParse((TopologyEditLayerBox.Text ?? string.Empty).Trim(), out var layer) || layer < 0)
                throw new InvalidOperationException("层级必须是大于等于 0 的整数。");

            var metadata = BuildTopologyMetadataDictionary();

            var payload = new
            {
                discipline,
                module = new
                {
                    id = _topologyEditorNodeId ?? node.NodeId,
                    name = (TopologyEditNameBox.Text ?? string.Empty).Trim(),
                    path = (TopologyEditPathBox.Text ?? string.Empty).Trim(),
                    layer,
                    parentModuleId = (TopologyEditParentBox.SelectedItem as TopologyParentOption)?.NodeId,
                    managedPaths = ParseMultilineList(TopologyEditManagedPathsBox.Text),
                    dependencies = ParseMultilineList(TopologyEditDependenciesBox.Text),
                    maintainer = EmptyToNull(TopologyEditMaintainerBox.Text),
                    summary = EmptyToNull(TopologyEditSummaryBox.Text),
                    boundary = GetSelectedBoundary(),
                    publicApi = ParseMultilineList(TopologyEditPublicApiBox.Text),
                    constraints = ParseMultilineList(TopologyEditConstraintsBox.Text),
                    metadata = metadata.Count == 0 ? null : metadata
                }
            };

            await PostJsonAsync(BuildLocalUrl("/api/modules"), payload);
            TopologyEditStatusText.Text = $"模块已保存：{payload.module.name}";
            await RefreshTopologyAsync();
            var refreshedNodeId = _topologyNodes.Values
                .FirstOrDefault(x => string.Equals(x.NodeId, _topologyEditorNodeId ?? node.NodeId, StringComparison.OrdinalIgnoreCase))?.Id
                ?? _topologyNodes.Values.FirstOrDefault(x => string.Equals(x.Label, payload.module.name, StringComparison.OrdinalIgnoreCase))?.Id;
            SelectTopologyNode(refreshedNodeId, syncGraph: true, syncList: true);
            TopologyEditStatusText.Text = $"模块已保存：{payload.module.name}";
        }
        catch (Exception ex)
        {
            TopologyEditStatusText.Text = $"保存失败：{ex.Message}";
        }
    }

    private void ReloadTopologyModuleEditor_OnClick(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedTopologyNodeId) || !_topologyNodes.TryGetValue(_selectedTopologyNodeId, out var node))
        {
            ResetTopologyEditor();
            TopologyEditStatusText.Text = "没有可重载的模块。";
            return;
        }

        var discardedChanges = _topologyEditorDirty;
        PopulateTopologyEditor(node);
        TopologyEditStatusText.Text = discardedChanges
            ? $"已恢复到当前已加载定义：{node.Label}；未保存修改已丢弃。"
            : $"已重载当前表单：{node.Label}";
    }

    private async void RefreshTopologyModuleEditorFromServer_OnClick(object? sender, RoutedEventArgs e)
    {
        await RefreshTopologyModuleEditorFromServerAsync();
    }

    private async Task RefreshTopologyModuleEditorFromServerAsync()
    {
        if (_project is null)
        {
            TopologyEditStatusText.Text = "未选择项目，无法读取运行时最新定义。";
            return;
        }

        if (string.IsNullOrWhiteSpace(_selectedTopologyNodeId) || !_topologyNodes.TryGetValue(_selectedTopologyNodeId, out var node))
        {
            ResetTopologyEditor();
            TopologyEditStatusText.Text = "没有可读取的模块。";
            return;
        }

        var discardedChanges = _topologyEditorDirty;
        var targetNodeId = _topologyEditorNodeId ?? node.NodeId;
        var targetName = node.Label;

        await RefreshTopologyAsync();

        var refreshedNodeId = _topologyNodes.Values
            .FirstOrDefault(x => string.Equals(x.NodeId, targetNodeId, StringComparison.OrdinalIgnoreCase))?.Id
            ?? _topologyNodes.Values.FirstOrDefault(x => string.Equals(x.Label, targetName, StringComparison.OrdinalIgnoreCase))?.Id;

        if (string.IsNullOrWhiteSpace(refreshedNodeId))
        {
            ResetTopologyEditor();
            TopologyEditStatusText.Text = $"运行时最新定义中未找到模块：{targetName}";
            return;
        }

        SelectTopologyNode(refreshedNodeId, syncGraph: true, syncList: true);
        TopologyEditStatusText.Text = discardedChanges
            ? $"已用运行时最新定义覆盖本地未保存修改：{ResolveNodeLabel(refreshedNodeId)}"
            : $"已读取运行时最新定义：{ResolveNodeLabel(refreshedNodeId)}";
    }

    private async void CopyTopologyMcpPreview_OnClick(object? sender, RoutedEventArgs e)
    {
        var text = TopologyMcpPreviewBox.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            TopologyEditStatusText.Text = "当前没有可复制的 MCP 调用。";
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard is null)
        {
            TopologyEditStatusText.Text = "当前环境不可用剪贴板。";
            return;
        }

        await topLevel.Clipboard.SetTextAsync(text);
        TopologyEditStatusText.Text = "已复制当前 MCP 调用预览。";
    }

    private IReadOnlyList<string> ResolveNodeDependencies(string nodeId)
    {
        if (!_topologyNodes.TryGetValue(nodeId, out _))
            return Array.Empty<string>();

        return _topologyEdges
            .Where(edge =>
                string.Equals(edge.From, nodeId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(ResolveTopologyRelationKey(edge), "dependency", StringComparison.OrdinalIgnoreCase))
            .Select(edge => ResolveNodeLabel(edge.To))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private void SetBoundarySelection(string? boundary)
    {
        var normalized = boundary?.Trim() ?? string.Empty;
        foreach (var item in TopologyEditBoundaryBox.Items.OfType<ComboBoxItem>())
        {
            var value = item.Content?.ToString() ?? string.Empty;
            if (!string.Equals(value, normalized, StringComparison.OrdinalIgnoreCase))
                continue;

            TopologyEditBoundaryBox.SelectedItem = item;
            return;
        }

        TopologyEditBoundaryBox.SelectedIndex = 0;
    }

    private string? GetSelectedBoundary()
    {
        var raw = (TopologyEditBoundaryBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
        return EmptyToNull(raw);
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

    private bool ShouldDisplayTopologyEdge(TopologyEdgeViewModel edge)
    {
        return ResolveTopologyRelationKey(edge) switch
        {
            "dependency" => TopologyDependencyFilter.IsChecked != false,
            "composition" => TopologyCompositionFilter.IsChecked != false || TopologyParentChildFilter.IsChecked != false,
            "aggregation" => TopologyAggregationFilter.IsChecked != false || TopologyParentChildFilter.IsChecked != false,
            "parentchild" => TopologyParentChildFilter.IsChecked != false,
            "collaboration" => TopologyCollaborationFilter.IsChecked != false,
            _ => true
        };
    }

    private static string ResolveTopologyRelationKey(TopologyEdgeViewModel edge)
    {
        var relation = (edge.Relation ?? string.Empty).Trim().ToLowerInvariant();
        if (relation == "dependency")
            return "dependency";
        if (relation == "collaboration")
            return "collaboration";
        if (relation == "parentchild")
            return "parentchild";
        if (relation == "containment")
        {
            var kind = (edge.Kind ?? string.Empty).Trim().ToLowerInvariant();
            if (kind is "composition" or "aggregation")
                return kind;
            return edge.IsComputed ? "aggregation" : "composition";
        }

        return relation;
    }

    private static string ResolveTopologyRelationLabel(TopologyEdgeViewModel edge)
    {
        return ResolveTopologyRelationKey(edge) switch
        {
            "dependency" => "依赖",
            "composition" => "组合",
            "aggregation" => "聚合",
            "parentchild" => "父子",
            "collaboration" => "协作",
            _ => edge.Relation
        };
    }

    private string ResolveNodeLabel(string nodeId)
    {
        return _topologyNodes.TryGetValue(nodeId, out var node)
            ? node.Label
            : nodeId;
    }

    private void ResetTopologyState()
    {
        _topologyNodes.Clear();
        _topologyEdges.Clear();
        _selectedTopologyNodeId = null;

        TopologyGraph.ClearTopology();
        TopologyListBox.ItemsSource = Array.Empty<TopologyModuleListItem>();
        TopologyRelationListBox.ItemsSource = Array.Empty<string>();
        TopologyDetailTitle.Text = "未选择节点";
        TopologyDetailMeta.Text = "-";
        TopologyDetailSummary.Text = "点击左侧节点查看节点详情。";
        TopologyScopeText.Text = "当前视图：全局根视图";
        ResetTopologyEditor();

        StatModulesText.Text = "-";
        StatDependenciesText.Text = "-";
        StatCollaborationText.Text = "-";
        StatDisciplinesText.Text = "-";
    }

    private static List<TopologyNodeViewModel> ParseTopologyNodes(JsonElement topology)
    {
        var nodes = new List<TopologyNodeViewModel>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (topology.TryGetProperty("project", out var project) && project.ValueKind == JsonValueKind.Object)
        {
            var projectId = GetString(project, "id", "project") ?? "project";
            var projectLabel = GetString(project, "name", "Project") ?? "Project";
            var projectSummary = GetString(project, "summary", "项目根节点：进入后查看部门层级。") ?? "项目根节点：进入后查看部门层级。";
            TryAddTopologyNode(nodes, seenIds, new TopologyNodeViewModel(
                projectId,
                projectId,
                projectLabel,
                "Project",
                "项目",
                "root",
                "项目",
                0,
                projectSummary,
                0,
                ManagedPathScopes: ParseStringArray(project, "managedPathScopes"),
                CanEdit: false));
        }

        if (topology.TryGetProperty("disciplines", out var disciplines) && disciplines.ValueKind == JsonValueKind.Array)
        {
            foreach (var discipline in disciplines.EnumerateArray())
            {
                var disciplineId = GetString(discipline, "id", string.Empty) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(disciplineId) || string.Equals(disciplineId, "root", StringComparison.OrdinalIgnoreCase))
                    continue;

                var displayName = GetString(discipline, "displayName", disciplineId) ?? disciplineId;
                var departmentNodeId = $"{DepartmentNodePrefix}{disciplineId}";
                TryAddTopologyNode(nodes, seenIds, new TopologyNodeViewModel(
                    departmentNodeId,
                    departmentNodeId,
                    displayName,
                    "Department",
                    "部门",
                    disciplineId,
                    displayName,
                    0,
                    $"{displayName} 部门节点：进入后查看该部门直属模块。",
                    0,
                    CanEdit: false));
            }
        }

        if (!topology.TryGetProperty("modules", out var modules) || modules.ValueKind != JsonValueKind.Array)
            return nodes;

        foreach (var module in modules.EnumerateArray())
        {
            var id = GetString(module, "name", string.Empty) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(id))
                continue;

            var nodeId = GetString(module, "nodeId", id) ?? id;
            var label = GetString(module, "displayName", id) ?? id;
            var type = GetString(module, "type", "Unknown") ?? "Unknown";
            var typeLabel = GetString(module, "typeLabel", type) ?? type;
            var discipline = GetString(module, "discipline", "unknown") ?? "unknown";
            var disciplineLabel = GetString(module, "disciplineDisplayName", discipline) ?? discipline;
            var summary = GetString(module, "summary", string.Empty) ?? string.Empty;
            var computedLayer = ParseNullableInt(module, "computedLayer") ??
                                ParseNullableInt(module, "layer");

            var depCount = 0;
            if (module.TryGetProperty("dependencies", out var deps) && deps.ValueKind == JsonValueKind.Array)
                depCount = deps.GetArrayLength();

            TryAddTopologyNode(nodes, seenIds, new TopologyNodeViewModel(
                id,
                nodeId,
                label,
                type,
                typeLabel,
                discipline,
                disciplineLabel,
                depCount,
                summary,
                computedLayer,
                RelativePath: GetString(module, "relativePath", null),
                ParentModuleId: GetString(module, "parentModuleId", GetString(module, "parentId", null)),
                ChildModuleIds: ParseStringArray(module, "childIds"),
                ManagedPathScopes: ParseStringArray(module, "managedPathScopes"),
                Maintainer: GetString(module, "maintainer", null),
                Boundary: GetString(module, "boundary", null),
                PublicApi: ParseStringArray(module, "publicApi"),
                Constraints: ParseStringArray(module, "constraints"),
                Workflow: ParseStringArray(module, "workflow"),
                Rules: ParseStringArray(module, "rules"),
                Prohibitions: ParseStringArray(module, "prohibitions"),
                Metadata: ParseStringDictionary(module, "metadata"),
                CanEdit: true));
        }

        return nodes;
    }

    private static List<TopologyEdgeViewModel> ParseTopologyEdges(JsonElement topology)
    {
        var edges = new List<TopologyEdgeViewModel>();

        if (topology.TryGetProperty("relationEdges", out var relationEdges) && relationEdges.ValueKind == JsonValueKind.Array)
        {
            AddTopologyEdges(edges, relationEdges, "dependency");
            return edges;
        }

        if (topology.TryGetProperty("edges", out var dependencyEdges) && dependencyEdges.ValueKind == JsonValueKind.Array)
            AddTopologyEdges(edges, dependencyEdges, "dependency");

        if (topology.TryGetProperty("containmentEdges", out var containmentEdges) && containmentEdges.ValueKind == JsonValueKind.Array)
            AddTopologyEdges(edges, containmentEdges, "containment");

        if (topology.TryGetProperty("collaborationEdges", out var collaborationEdges) && collaborationEdges.ValueKind == JsonValueKind.Array)
            AddTopologyEdges(edges, collaborationEdges, "collaboration");

        return edges;
    }

    private static void AddTopologyEdges(List<TopologyEdgeViewModel> target, JsonElement source, string defaultRelation)
    {
        foreach (var edge in source.EnumerateArray())
        {
            var from = GetString(edge, "from", string.Empty) ?? string.Empty;
            var to = GetString(edge, "to", string.Empty) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
                continue;

            var relation = GetString(edge, "relation", defaultRelation) ?? defaultRelation;
            var kind = GetString(edge, "kind", null);
            var isComputed = edge.TryGetProperty("isComputed", out var computed) && computed.ValueKind == JsonValueKind.True;

            target.Add(new TopologyEdgeViewModel(from, to, relation, isComputed, kind));
        }
    }

    private static void TryAddTopologyNode(
        List<TopologyNodeViewModel> nodes,
        HashSet<string> seenIds,
        TopologyNodeViewModel node)
    {
        if (!seenIds.Add(node.Id))
            return;

        nodes.Add(node);
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

        var selectedRecentRoot = ResolveSelectedRecent()?.ProjectRoot;
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
            Title = "选择目标项目根目录（必须包含 .project.dna/project.json）",
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

        SelectedProjectText.Text = $"目标项目: {project.ProjectName}";
        LaunchModeText.Text = $"启动方式: 桌面主宿主 + 嵌入式 Client API | MCP: {ClientRuntimeConstants.ApiBaseUrl}/mcp | CLI: dna_client cli";
        OverviewModeText.Text = BuildWorkspaceModeText(project);
        ToolingHubText.Text = $"当前项目 {project.ProjectName} 已连接本地 5052 运行时，可为 IDE Agent 暴露 API、CLI 与 /mcp。";
        ConnectionServerText.Text = BuildRuntimeEndpointSummary();

        UpdateWindowTitle();
    }

    private DesktopProjectConfig EnsureProjectSelected()
    {
        return _project ?? throw new InvalidOperationException("请先选择目标项目并完成 .project.dna/project.json 校验。");
    }

    private static async Task<bool> IsClientOnlineAsync()
    {
        try
        {
            using var response = await StatusHttp.GetAsync(BuildLocalUrl("/api/client/status"));
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

    private void ApplyConnectionState(bool clientOnline)
    {
        if (_project is null || !_connectionAccess.HasProject)
        {
            AccessStateText.Text = "写入边界: 未选择项目";
            ConnectionStatusText.Text = "等待项目加载";
            ConnectionRoleText.Text = "模式: -";
            ConnectionIpText.Text = "来源: -";
            ConnectionServerText.Text = "尚未选择项目。";
            ConnectionNoteText.Text = "本地状态页会集中显示 5052 运行时、当前项目与写入边界。";
            ConnectionRecommendationText.Text = "加载项目后自动读取本地 /api/status 与 /api/connection/access。";
            MemoryAccessText.Text = "当前记忆库位于项目 .project.dna；写入能力由本地运行时状态决定。";
            AddMemoryButton.IsEnabled = false;
            StatusText.Text = "请先在项目加载页选择项目，进入工作区后会自动拉起本地 5052 运行时。";
            OverviewPrimaryActionText.Text = "先加载一个包含 .project.dna/project.json 的项目，然后确认本地运行时与知识库状态。";
            OverviewWorkflowText.Text = "推荐顺序：概览 -> 本地状态 -> 知识图谱/记忆 -> 连接Agent。";
            OverviewRecommendationText.Text = "当前以单人本地管理员 MVP 为主，优先把桌面主路径稳定下来。";
            return;
        }

        ConnectionServerText.Text = BuildRuntimeEndpointSummary();

        if (!_connectionAccess.RuntimeOnline)
        {
            AccessStateText.Text = "写入边界: 运行时离线";
            ConnectionStatusText.Text = "本地运行时离线";
            ConnectionRoleText.Text = "模式: -";
            ConnectionIpText.Text = "来源: -";
            ConnectionNoteText.Text = "当前无法从本地 5052 运行时读取项目状态。";
            ConnectionRecommendationText.Text = "请先恢复桌面宿主或释放 5052 端口，再重新进入项目。";
            MemoryAccessText.Text = "运行时离线时，记忆页仅保留浏览入口，本地写入已禁用。";
            AddMemoryButton.IsEnabled = false;
            StatusText.Text = "本地运行时尚未连接成功，建议先恢复桌面宿主再继续。";
            OverviewPrimaryActionText.Text = clientOnline
                ? "桌面宿主仍在运行，但本地知识库状态未同步；下一步请刷新当前项目。"
                : "请先确认桌面宿主已经启动，并且 5052 没有被其他进程占用。";
            OverviewWorkflowText.Text = "运行时恢复后，重新打开“本地状态”确认项目与写入边界。";
            OverviewRecommendationText.Text = "单机 MVP 优先保证“项目加载 -> 本地 5052 -> 图谱/记忆/MCP”这条主路径稳定。";
            return;
        }

        if (!_connectionAccess.Allowed)
        {
            var reason = string.IsNullOrWhiteSpace(_connectionAccess.Reason) ? "当前本地运行时未开放写入" : _connectionAccess.Reason;
            AccessStateText.Text = $"写入边界: 只读（{reason}）";
            ConnectionStatusText.Text = "本地运行时已连接，但当前模式只读";
            ConnectionRoleText.Text = "模式: 只读";
            ConnectionIpText.Text = $"来源: {_connectionAccess.RemoteIp}";
            ConnectionNoteText.Text = $"运行时说明：{reason}";
            ConnectionRecommendationText.Text = "请确认当前项目是否完整加载，并检查本地运行时的写入策略。";
            MemoryAccessText.Text = "当前运行态为只读，记忆页仅用于查看，本地写入已禁用。";
            AddMemoryButton.IsEnabled = false;
            StatusText.Text = "本地运行时已经连接，但当前模式不允许写入。";
            OverviewPrimaryActionText.Text = "下一步建议先检查项目配置与写入边界，再继续修改知识。";
            OverviewWorkflowText.Text = "只读模式下可继续浏览图谱、记忆与 MCP 工具。";
            OverviewRecommendationText.Text = "单机 MVP 默认推荐以本地管理员模式运行，避免手动编辑与 MCP 写入出现分叉。";
            return;
        }

        var roleLabel = DescribeRole(_connectionAccess.Role);
        AccessStateText.Text = $"写入边界: {roleLabel}";
        ConnectionStatusText.Text = "本地运行时已连接";
        ConnectionRoleText.Text = $"模式: {roleLabel}";
        ConnectionIpText.Text = $"来源: {_connectionAccess.EntryName} | {_connectionAccess.RemoteIp}";
        ConnectionNoteText.Text = string.IsNullOrWhiteSpace(_connectionAccess.Note)
            ? $"运行时来源：{_connectionAccess.EntryName}"
            : $"运行时来源：{_connectionAccess.EntryName} | 备注：{_connectionAccess.Note}";

        if (_connectionAccess.IsAdmin)
        {
            ConnectionRecommendationText.Text = "当前为本地管理员模式，可直接维护项目知识图谱、记忆，并继续使用连接Agent能力。";
            MemoryAccessText.Text = "当前运行态允许直接写入本地记忆库。";
            AddMemoryButton.IsEnabled = true;
            StatusText.Text = "主路径已打通，可以继续浏览知识图谱、查看记忆并写入本地知识。";
            OverviewPrimaryActionText.Text = "当前已具备本地管理员能力，建议优先检查图谱、记忆和 Agent 连接配置是否完整。";
            OverviewWorkflowText.Text = "推荐顺序：本地状态确认 -> 知识图谱预览 -> 记忆维护 -> 连接Agent分发到 IDE。";
            OverviewRecommendationText.Text = "如果只是单人本地 MVP，这已经是最完整的闭环路径。";
            return;
        }

        ConnectionRecommendationText.Text = "当前不是本地管理员模式，可浏览本地知识并使用 CLI / MCP；写入入口保持关闭。";
        MemoryAccessText.Text = $"当前模式为 {roleLabel}，此版本只开放本地知识浏览，写入按钮已禁用。";
        AddMemoryButton.IsEnabled = false;
        StatusText.Text = "当前可浏览本地知识并使用 CLI / MCP，本地写入仍需管理员模式。";
        OverviewPrimaryActionText.Text = "当前连接已可用，下一步建议先浏览知识图谱与记忆，再完成 IDE Agent 连接。";
        OverviewWorkflowText.Text = "后续若重新引入协作流，再把多人审核与权限模型叠加到这条本地主路径上。";
        OverviewRecommendationText.Text = "当前桌面 MVP 先把浏览、编辑、MCP、CLI 这条单机主路径打稳。";
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

    private static int ResolveNodeLayer(TopologyNodeViewModel node)
    {
        if (node.ComputedLayer.HasValue)
            return node.ComputedLayer.Value;

        return node.Type.ToLowerInvariant() switch
        {
            "project" => 0,
            "department" => 0,
            "technical" => 1,
            "gateway" => 1,
            "team" => 2,
            _ => 1
        };
    }

    private void ApplyTopologyRelationFilter()
    {
        var dependency = TopologyDependencyFilter.IsChecked != false;
        var composition = TopologyCompositionFilter.IsChecked != false;
        var aggregation = TopologyAggregationFilter.IsChecked != false;
        var parentChild = TopologyParentChildFilter.IsChecked != false;
        var collaboration = TopologyCollaborationFilter.IsChecked != false;

        if (!dependency && !composition && !aggregation && !parentChild && !collaboration)
        {
            dependency = true;
            TopologyDependencyFilter.IsChecked = true;
        }

        TopologyGraph.SetRelationFilter(
            dependency,
            composition,
            aggregation,
            parentChild,
            collaboration);

        if (_topologyNodes.Count > 0)
            UpdateTopologyScopeState(preferredNodeId: _selectedTopologyNodeId);
    }

    private sealed record TopologyModuleListItem(string Key, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record TopologyDisciplineOption(string Id, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record TopologyParentOption(string? NodeId, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record RecentProjectListItem(DesktopRecentProjectEntry Entry)
    {
        public string ProjectRoot => Entry.ProjectRoot;

        public string Title
        {
            get
            {
                var lastOpened = Entry.LastOpenedAtUtc.ToLocalTime().ToString("MM-dd HH:mm");
                return $"{Entry.ProjectName}  [{lastOpened}]";
            }
        }

        public override string ToString() => $"{Title}\n{ProjectRoot}";
    }

    private sealed record ConnectionAccessState(
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
