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
    private readonly DesktopRecentProjectsStore _recentProjectsStore;
    private readonly Dictionary<string, TopologyNodeViewModel> _topologyNodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<TopologyEdgeViewModel> _topologyEdges = [];

    private DesktopProjectConfig? _project;
    private string? _selectedTopologyNodeId;
    private List<DesktopRecentProjectEntry> _recentProjects = [];

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

        TopologyGraph.NodeSelected += TopologyGraph_OnNodeSelected;

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _statusTimer.Tick += async (_, _) => await RefreshRuntimeStatusAsync();
        _statusTimer.Start();

        Opened += async (_, _) => await OnOpenedAsync();
        Closed += (_, _) => _statusTimer.Stop();
    }

    private async Task OnOpenedAsync()
    {
        RefreshRecentProjects();
        ShowProjectLoadPage("请选择项目。加载后会进入工作区。");
        await RefreshRuntimeStatusAsync();
    }

    private void InitializeDefaults()
    {
        SelectedProjectText.Text = "目标项目: 未选择";
        ClientStateText.Text = "Client: 检查中...";
        ServerStateText.Text = "Server: 检查中...";
        AccessStateText.Text = "权限: 检查中...";
        StatusText.Text = "请先在项目加载页选择项目，进入工作区后将自动连接服务器。";

        TopologySummaryText.Text = "尚未加载拓扑。";
        TopologyDetailTitle.Text = "未选择节点";
        TopologyDetailMeta.Text = "-";
        TopologyDetailSummary.Text = "点击左侧节点查看模块详情。";
        TopologyRelationListBox.ItemsSource = Array.Empty<string>();
        TopologyListBox.ItemsSource = Array.Empty<TopologyModuleListItem>();
        StatModulesText.Text = "-";
        StatDependenciesText.Text = "-";
        StatCollaborationText.Text = "-";
        StatDisciplinesText.Text = "-";

        MemoryStatusText.Text = "记忆区就绪。";
        ToolingStatusText.Text = "工具区就绪。";
        McpSummaryText.Text = "MCP 清单未加载。";

        RecentCountText.Text = "最近项目（0）";
        RecentProjectsListBox.ItemsSource = Array.Empty<RecentProjectListItem>();
        RecentProjectHintText.Text = "尚未选择最近项目。你也可以点击“打开项目目录...”加载新项目。";
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
            LoaderStatusText.Text = "请先在最近项目列表中选择一项。";
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

    private async Task LoadProjectAsync(string projectRoot)
    {
        try
        {
            var project = DesktopProjectConfig.Load(projectRoot);
            project.EnsureWorkspaceConfig();

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
            RecentProjectHintText.Text = "尚未选择最近项目。你也可以点击“打开项目目录...”加载新项目。";
            return;
        }

        var lastOpened = entry.LastOpenedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        RecentProjectHintText.Text =
            $"项目：{entry.ProjectName}\n" +
            $"目录：{entry.ProjectRoot}\n" +
            $"服务：{entry.ServerBaseUrl}\n" +
            $"上次打开：{lastOpened}";
    }

    private async void RefreshTopology_OnClick(object? sender, RoutedEventArgs e)
    {
        await RefreshTopologyAsync();
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
            ResetTopologyState();
            return;
        }

        try
        {
            var topology = await GetJsonAsync($"{_project.ServerBaseUrl}/api/topology");
            TopologySummaryText.Text = GetString(topology, "summary", "拓扑已加载")!;

            var nodes = ParseTopologyNodes(topology);
            var edges = ParseTopologyEdges(topology);
            var modulesListItems = nodes
                .OrderBy(n => n.DisciplineLabel, StringComparer.OrdinalIgnoreCase)
                .ThenBy(n => n.Label, StringComparer.OrdinalIgnoreCase)
                .Select(n => new TopologyModuleListItem(
                    n.Id,
                    $"{n.Label} | L{ResolveNodeLayer(n)} | {n.TypeLabel} | {n.DisciplineLabel} | deps={n.DependencyCount}"))
                .ToList();

            _topologyNodes.Clear();
            foreach (var node in nodes)
                _topologyNodes[node.Id] = node;

            _topologyEdges.Clear();
            _topologyEdges.AddRange(edges);

            TopologyGraph.SetTopology(nodes, edges);
            ApplyTopologyRelationFilter();
            TopologyListBox.ItemsSource = modulesListItems;

            var dependencyCount = edges.Count(e => string.Equals(e.Relation, "dependency", StringComparison.OrdinalIgnoreCase));
            var collaborationCount = edges.Count(e => string.Equals(e.Relation, "collaboration", StringComparison.OrdinalIgnoreCase));
            var disciplinesCount = topology.TryGetProperty("disciplines", out var disciplines) && disciplines.ValueKind == JsonValueKind.Array
                ? disciplines.GetArrayLength()
                : nodes.Select(n => n.DisciplineLabel).Distinct(StringComparer.OrdinalIgnoreCase).Count();

            StatModulesText.Text = nodes.Count.ToString();
            StatDependenciesText.Text = dependencyCount.ToString();
            StatCollaborationText.Text = collaborationCount.ToString();
            StatDisciplinesText.Text = disciplinesCount.ToString();

            if (!string.IsNullOrWhiteSpace(_selectedTopologyNodeId) && _topologyNodes.ContainsKey(_selectedTopologyNodeId))
            {
                SelectTopologyNode(_selectedTopologyNodeId, syncGraph: true, syncList: true);
                return;
            }

            if (modulesListItems.Count > 0)
            {
                SelectTopologyNode(modulesListItems[0].Key, syncGraph: true, syncList: true);
                return;
            }

            RenderTopologyDetails(null);
        }
        catch (Exception ex)
        {
            TopologySummaryText.Text = $"拓扑加载失败：{ex.Message}";
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
        if (_project is null)
        {
            CursorStateText.Text = "Cursor: 未加载项目";
            CodexStateText.Text = "Codex: 未加载项目";
            ToolingStatusText.Text = "请先加载项目工作区。";
            return;
        }

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

    private void SelectTopologyNode(string? nodeId, bool syncGraph, bool syncList)
    {
        if (string.IsNullOrWhiteSpace(nodeId) || !_topologyNodes.ContainsKey(nodeId))
        {
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

    private void RenderTopologyDetails(string? nodeId)
    {
        if (string.IsNullOrWhiteSpace(nodeId) || !_topologyNodes.TryGetValue(nodeId, out var node))
        {
            TopologyDetailTitle.Text = "未选择节点";
            TopologyDetailMeta.Text = "-";
            TopologyDetailSummary.Text = "点击左侧节点查看模块详情。";
            TopologyRelationListBox.ItemsSource = Array.Empty<string>();
            return;
        }

        TopologyDetailTitle.Text = node.Label;
        TopologyDetailMeta.Text = $"L{ResolveNodeLayer(node)} | {node.TypeLabel} | {node.DisciplineLabel} | deps {node.DependencyCount}";
        TopologyDetailSummary.Text = string.IsNullOrWhiteSpace(node.Summary)
            ? "暂无摘要。"
            : node.Summary;

        var outgoing = _topologyEdges.Where(e => string.Equals(e.From, nodeId, StringComparison.OrdinalIgnoreCase)).ToList();
        var incoming = _topologyEdges.Where(e => string.Equals(e.To, nodeId, StringComparison.OrdinalIgnoreCase)).ToList();

        var lines = new List<string>
        {
            $"出边: {outgoing.Count} | 入边: {incoming.Count}",
            string.Empty
        };

        lines.AddRange(outgoing
            .OrderBy(e => e.Relation, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => ResolveNodeLabel(e.To), StringComparer.OrdinalIgnoreCase)
            .Select(e => $"OUT [{e.Relation}] -> {ResolveNodeLabel(e.To)}"));

        lines.AddRange(incoming
            .OrderBy(e => e.Relation, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => ResolveNodeLabel(e.From), StringComparer.OrdinalIgnoreCase)
            .Select(e => $"IN  [{e.Relation}] <- {ResolveNodeLabel(e.From)}"));

        TopologyRelationListBox.ItemsSource = lines;
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
        TopologyDetailSummary.Text = "点击左侧节点查看模块详情。";

        StatModulesText.Text = "-";
        StatDependenciesText.Text = "-";
        StatCollaborationText.Text = "-";
        StatDisciplinesText.Text = "-";
    }

    private static List<TopologyNodeViewModel> ParseTopologyNodes(JsonElement topology)
    {
        var nodes = new List<TopologyNodeViewModel>();

        if (!topology.TryGetProperty("modules", out var modules) || modules.ValueKind != JsonValueKind.Array)
            return nodes;

        foreach (var module in modules.EnumerateArray())
        {
            var id = GetString(module, "name", string.Empty) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(id))
                continue;

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

            nodes.Add(new TopologyNodeViewModel(
                id,
                label,
                type,
                typeLabel,
                discipline,
                disciplineLabel,
                depCount,
                summary,
                computedLayer));
        }

        return nodes;
    }

    private static List<TopologyEdgeViewModel> ParseTopologyEdges(JsonElement topology)
    {
        var edges = new List<TopologyEdgeViewModel>();

        JsonElement source;
        if (topology.TryGetProperty("relationEdges", out var relationEdges) && relationEdges.ValueKind == JsonValueKind.Array)
        {
            source = relationEdges;
        }
        else if (topology.TryGetProperty("edges", out var dependencyEdges) && dependencyEdges.ValueKind == JsonValueKind.Array)
        {
            source = dependencyEdges;
        }
        else
        {
            return edges;
        }

        foreach (var edge in source.EnumerateArray())
        {
            var from = GetString(edge, "from", string.Empty) ?? string.Empty;
            var to = GetString(edge, "to", string.Empty) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
                continue;

            var relation = GetString(edge, "relation", "dependency") ?? "dependency";
            var isComputed = edge.TryGetProperty("isComputed", out var computed) && computed.ValueKind == JsonValueKind.True;

            edges.Add(new TopologyEdgeViewModel(from, to, relation, isComputed));
        }

        return edges;
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

        SelectedProjectText.Text = $"目标项目: {project.ProjectName}";
        LaunchModeText.Text = $"启动方式: 嵌入单进程 | MCP: http://127.0.0.1:5052/mcp";

        UpdateWindowTitle();
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

    private static int ResolveNodeLayer(TopologyNodeViewModel node)
    {
        if (node.ComputedLayer.HasValue)
            return node.ComputedLayer.Value;

        return node.Type.ToLowerInvariant() switch
        {
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
    }

    private sealed record TopologyModuleListItem(string Key, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record RecentProjectListItem(DesktopRecentProjectEntry Entry)
    {
        public override string ToString()
        {
            var lastOpened = Entry.LastOpenedAtUtc.ToLocalTime().ToString("MM-dd HH:mm");
            return $"{Entry.ProjectName}  [{lastOpened}]\n{Entry.ProjectRoot}";
        }
    }
}
