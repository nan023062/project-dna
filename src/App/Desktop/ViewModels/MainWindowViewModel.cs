using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dna.App.Desktop.Services;
using Dna.App.Services;

namespace Dna.App.Desktop.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IDnaApiClient _apiClient;

    public Func<Task<string?>>? PickProjectFolderAsync { get; set; }
    public Func<string, Task>? LoadProjectAsyncHandler { get; set; }
    public Func<IReadOnlyList<DesktopRecentProjectEntry>>? LoadRecentProjectsHandler { get; set; }
    public Action<string>? RemoveRecentProjectHandler { get; set; }
    public Action? CloseWindowHandler { get; set; }

    [ObservableProperty]
    private string _projectName = "未选择";

    [ObservableProperty]
    private string _projectRoot = string.Empty;

    [ObservableProperty]
    private bool _isAppOnline;

    [ObservableProperty]
    private string _windowTitle = "Agentic OS - 项目加载";

    [ObservableProperty]
    private bool _isProjectLoadVisible = true;

    [ObservableProperty]
    private bool _isWorkspaceVisible;

    [ObservableProperty]
    private string _appStateText = "App: 检查中...";

    [ObservableProperty]
    private string _serverStateText = "本地知识库: 检查中...";

    [ObservableProperty]
    private string _accessStateText = "写入边界: 检查中...";

    [ObservableProperty]
    private string _selectedProjectText = "目标项目: 未选择";

    [ObservableProperty]
    private string _launchModeText = "启动方式: 桌面主宿主 + 嵌入式 App API";

    [ObservableProperty]
    private string _overviewModeText = "桌面主宿主负责项目切换、知识图谱预览、记忆维护，以及本地 CLI / MCP 连接。";

    [ObservableProperty]
    private string _statusText = "请先在项目加载页选择项目，进入工作区后会自动拉起本地 5052 运行时。";

    [ObservableProperty]
    private string _overviewPrimaryActionText = "先加载一个项目目录；如缺少 .agentic-os，桌面宿主会自动创建并初始化运行数据。";

    [ObservableProperty]
    private string _overviewWorkflowText = "推荐顺序：概览 -> 本地状态 -> 知识图谱/记忆 -> 连接Agent。";

    [ObservableProperty]
    private string _overviewRecommendationText = "当前以单人本地管理员 MVP 为主，优先把桌面主路径稳定下来。";

    [ObservableProperty]
    private string _connectionStatusText = "等待项目加载";

    [ObservableProperty]
    private string _connectionRoleText = "模式: -";

    [ObservableProperty]
    private string _connectionIpText = "来源: -";

    [ObservableProperty]
    private string _connectionServerText = "尚未选择项目。";

    [ObservableProperty]
    private string _connectionNoteText = "本地状态页会集中显示 5052 运行时、当前项目与写入边界。";

    [ObservableProperty]
    private string _connectionRecommendationText = "加载项目后自动读取本地 /api/status 与 /api/connection/access。";

    [ObservableProperty]
    private string _loaderStatusText = "请选择项目。";

    [ObservableProperty]
    private string _recentCountText = "最近项目（0）";

    [ObservableProperty]
    private string _recentProjectHintText = "尚未选择最近项目。你也可以双击最近项目直接进入，或点击“打开项目目录...”加载新项目。";

    [ObservableProperty]
    private ObservableCollection<RecentProjectItemViewModel> _recentProjects = [];

    [ObservableProperty]
    private RecentProjectItemViewModel? _selectedRecentProject;

    public WorkspaceExplorerViewModel WorkspaceExplorer { get; }
    public TopologyViewModel Topology { get; }
    public MemoryViewModel Memory { get; }
    public ChatViewModel Chat { get; }
    public ToolingViewModel Tooling { get; }

    public MainWindowViewModel(IDnaApiClient apiClient)
    {
        _apiClient = apiClient;
        WorkspaceExplorer = new WorkspaceExplorerViewModel(apiClient);
        Topology = new TopologyViewModel(apiClient);
        Memory = new MemoryViewModel(apiClient);
        Chat = new ChatViewModel(apiClient);
        Tooling = new ToolingViewModel(apiClient);
    }

    partial void OnSelectedRecentProjectChanged(RecentProjectItemViewModel? value)
    {
        if (value is null)
        {
            RecentProjectHintText = "尚未选择最近项目。你也可以双击最近项目直接进入，或点击“打开项目目录...”加载新项目。";
            return;
        }

        var lastOpened = value.Entry.LastOpenedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        RecentProjectHintText =
            $"项目：{value.Entry.ProjectName}\n" +
            $"目录：{value.Entry.ProjectRoot}\n" +
            $"运行时：{AppRuntimeConstants.ApiBaseUrl}\n" +
            $"上次打开：{lastOpened}";
    }

    [RelayCommand]
    private async Task RefreshRuntimeStatusAsync()
    {
        try
        {
            await _apiClient.GetAsync("/api/app/status");
            IsAppOnline = true;
            AppStateText = $"App: 在线（{AppRuntimeConstants.ApiBaseUrl}）";
        }
        catch
        {
            IsAppOnline = false;
            AppStateText = "App: 离线";
        }
    }

    [RelayCommand]
    private async Task OpenProjectFolderAsync()
    {
        if (PickProjectFolderAsync is null || LoadProjectAsyncHandler is null)
            return;

        var selected = await PickProjectFolderAsync();
        if (string.IsNullOrWhiteSpace(selected))
        {
            LoaderStatusText = "已取消项目选择。";
            return;
        }

        await LoadProjectAsyncHandler(selected);
    }

    [RelayCommand]
    private async Task LoadSelectedRecentAsync()
    {
        if (SelectedRecentProject is null || LoadProjectAsyncHandler is null)
        {
            LoaderStatusText = "请先在最近项目列表中选择一项。";
            return;
        }

        await LoadProjectAsyncHandler(SelectedRecentProject.ProjectRoot);
    }

    [RelayCommand]
    private void SwitchWorkspace()
    {
        RefreshRecentProjects(ProjectRoot);
        ShowProjectLoadPage(string.IsNullOrWhiteSpace(ProjectRoot)
            ? "请选择项目。"
            : $"当前工作区：{ProjectName}。选择其他项目后会自动切换。");
    }

    [RelayCommand]
    private void ReloadRecentProjects()
    {
        RefreshRecentProjects(ProjectRoot);
        LoaderStatusText = "最近项目列表已刷新。";
    }

    [RelayCommand]
    private void RemoveRecentProject(string? projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot) || RemoveRecentProjectHandler is null)
        {
            LoaderStatusText = "未找到要移除的项目。";
            return;
        }

        var existing = RecentProjects.FirstOrDefault(item =>
            string.Equals(item.ProjectRoot, projectRoot, StringComparison.OrdinalIgnoreCase));
        var name = existing?.Entry.ProjectName ?? projectRoot;

        RemoveRecentProjectHandler(projectRoot);
        RefreshRecentProjects();
        LoaderStatusText = $"已移除：{name}";
    }

    [RelayCommand]
    private void Exit() => CloseWindowHandler?.Invoke();

    public void RefreshRecentProjects(string? preferredProjectRoot = null)
    {
        var loaded = LoadRecentProjectsHandler?.Invoke() ?? [];
        RecentProjects.Clear();
        foreach (var item in loaded.Select(entry => new RecentProjectItemViewModel(entry)))
        {
            RecentProjects.Add(item);
        }

        RecentCountText = $"最近项目（{RecentProjects.Count}）";

        if (RecentProjects.Count == 0)
        {
            SelectedRecentProject = null;
            return;
        }

        SelectedRecentProject = !string.IsNullOrWhiteSpace(preferredProjectRoot)
            ? RecentProjects.FirstOrDefault(item =>
                string.Equals(item.ProjectRoot, preferredProjectRoot, StringComparison.OrdinalIgnoreCase))
              ?? RecentProjects[0]
            : RecentProjects[0];
    }

    public void ShowProjectLoadPage(string status)
    {
        IsProjectLoadVisible = true;
        IsWorkspaceVisible = false;
        LoaderStatusText = status;
        UpdateWindowTitle();
    }

    public void ShowWorkspacePage()
    {
        IsProjectLoadVisible = false;
        IsWorkspaceVisible = true;
        UpdateWindowTitle();
    }

    public void UpdateWindowTitle()
    {
        WindowTitle = IsWorkspaceVisible && !string.IsNullOrWhiteSpace(ProjectName)
            ? $"Agentic OS - {ProjectName}"
            : "Agentic OS - 项目加载";
    }
}
