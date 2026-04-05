using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dna.App.Desktop.Services;
using Dna.App.Services;
using Dna.ExternalAgent.Interfaces.Api;

namespace Dna.App.Desktop.ViewModels;

public partial class ToolingViewModel : ObservableObject
{
    private readonly IDnaApiClient _apiClient;

    [ObservableProperty]
    private string _cursorStateText = "Cursor: -";

    [ObservableProperty]
    private string _codexStateText = "Codex: -";

    [ObservableProperty]
    private string _claudeCodeStateText = "Claude Code: -";

    [ObservableProperty]
    private string _copilotStateText = "GitHub Copilot: -";

    [ObservableProperty]
    private ObservableCollection<ToolingTargetStatusViewModel> _targetDetails = new();

    [ObservableProperty]
    private string _toolingHubText = "App 保留本地 5052 运行时，既服务桌面宿主内部调用，也向外暴露 CLI 与 /mcp。";

    [ObservableProperty]
    private string _toolingTargetsSummaryText = "外置 Agent 目标状态未加载。";

    [ObservableProperty]
    private string _toolingStatusText = "工具区就绪。";

    [ObservableProperty]
    private string _mcpSummaryText = "MCP 清单未加载。";

    [ObservableProperty]
    private ObservableCollection<string> _mcpTools = new();

    [ObservableProperty]
    private string _projectRoot = string.Empty;

    public ToolingViewModel(IDnaApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public void Reset()
    {
        ToolingHubText = "App 保留本地 5052 运行时，既服务桌面宿主内部调用，也向外暴露 CLI 与 /mcp。";
        ToolingStatusText = "工具区就绪。";
        McpSummaryText = "MCP 清单未加载。";
        CursorStateText = "Cursor: -";
        CodexStateText = "Codex: -";
        ClaudeCodeStateText = "Claude Code: -";
        CopilotStateText = "GitHub Copilot: -";
        ToolingTargetsSummaryText = "外置 Agent 目标状态未加载。";
        TargetDetails.Clear();
        McpTools.Clear();
    }

    [RelayCommand]
    public async Task RefreshToolingStatusAsync(string? projectRoot)
    {
        ProjectRoot = projectRoot ?? string.Empty;
        if (string.IsNullOrWhiteSpace(ProjectRoot))
        {
            CursorStateText = "Cursor: 未加载项目";
            CodexStateText = "Codex: 未加载项目";
            ClaudeCodeStateText = "Claude Code: 未加载项目";
            CopilotStateText = "GitHub Copilot: 未加载项目";
            ToolingHubText = "请先加载项目工作区，再为 IDE 安装本地 DNA App 连接配置。";
            ToolingTargetsSummaryText = "请先加载项目工作区。";
            ToolingStatusText = "请先加载项目工作区。";
            TargetDetails.Clear();
            return;
        }

        try
        {
            var tooling = await _apiClient.GetAsync<ExternalAgentToolingListResponse>("/api/app/tooling/list");
            CursorStateText = "Cursor: 未安装";
            CodexStateText = "Codex: 未安装";
            ClaudeCodeStateText = "Claude Code: 未安装";
            CopilotStateText = "GitHub Copilot: 未安装";
            TargetDetails.Clear();

            foreach (var target in tooling.Targets)
            {
                var detail = new ToolingTargetStatusViewModel(
                    ProductId: target.ProductId,
                    DisplayName: target.DisplayName,
                    StateLabel: target.StateLabel,
                    MetaLine: target.MetaLine,
                    SummaryLine: target.SummaryLine,
                    IsInstalled: target.Installed);

                SetTargetStateText(target.ProductId, $"{target.DisplayName}: {target.StateLabel}");
                TargetDetails.Add(detail);
            }

            var workspaceRoot = tooling.WorkspaceRoot;
            var readyCount = tooling.ReadyCount;
            var pendingCount = tooling.PendingCount;
            ToolingTargetsSummaryText = $"已检测 {TargetDetails.Count} 个外置 Agent 目标：{readyCount} 个已就绪，{pendingCount} 个待处理。";
            ToolingHubText = $"本地 5052 运行时已就绪。当前工作区：{workspaceRoot}；CLI：agentic-os cli；MCP 入口：{AppRuntimeConstants.ApiBaseUrl}/mcp";
            ToolingStatusText = $"工具状态已刷新。工作区：{workspaceRoot}";
        }
        catch (Exception ex)
        {
            CursorStateText = "Cursor: 错误";
            CodexStateText = "Codex: 错误";
            ClaudeCodeStateText = "Claude Code: 错误";
            CopilotStateText = "GitHub Copilot: 错误";
            ToolingTargetsSummaryText = "外置 Agent 目标状态刷新失败。";
            ToolingStatusText = $"工具状态刷新失败：{ex.Message}";
            TargetDetails.Clear();
        }
    }

    [RelayCommand]
    public async Task InstallToolingAsync(string target)
    {
        if (string.IsNullOrWhiteSpace(ProjectRoot))
        {
            ToolingStatusText = "App 未运行，请先启动 App。";
            return;
        }

        try
        {
            ToolingStatusText = $"正在选择 {FormatToolingTargetName(target)} 安装目录...";
            var picked = await _apiClient.PostAsync<ExternalAgentFolderPickResponse>("/api/app/tooling/select-folder", new
            {
                defaultWorkspaceRoot = ProjectRoot,
                prompt = $"选择要安装 {FormatToolingTargetName(target)} 工作流配置的项目目录"
            });

            if (!picked.Selected)
            {
                ToolingStatusText = "已取消目录选择。";
                return;
            }

            var workspaceRoot = picked.WorkspaceRoot;
            if (string.IsNullOrWhiteSpace(workspaceRoot))
                throw new InvalidOperationException("未获取到有效工作区目录。");

            var install = await _apiClient.PostAsync<ExternalAgentToolingInstallResponse>("/api/app/tooling/install", new
            {
                target,
                replaceExisting = true,
                workspaceRoot
            });

            ToolingStatusText = $"{FormatToolingTargetName(target)} 安装完成（报告 {install.Reports.Count} 条），目录：{workspaceRoot}";
            await RefreshToolingStatusAsync(ProjectRoot);
        }
        catch (Exception ex)
        {
            ToolingStatusText = $"安装失败：{ex.Message}";
        }
    }

    [RelayCommand]
    public async Task RefreshMcpToolsAsync()
    {
        try
        {
            var catalog = await _apiClient.GetAsync<ExternalAgentMcpToolCatalogResponse>("/api/app/mcp/tools");
            var endpoint = string.IsNullOrWhiteSpace(catalog.McpEndpoint)
                ? $"{AppRuntimeConstants.ApiBaseUrl}/mcp"
                : catalog.McpEndpoint;

            McpTools.Clear();
            foreach (var tool in catalog.Tools)
            {
                McpTools.Add($"[{tool.Group}] {tool.Name} - {tool.Description}");
            }

            McpSummaryText = $"MCP 入口：{endpoint}，工具数：{McpTools.Count}";
        }
        catch (Exception ex)
        {
            McpSummaryText = $"MCP 清单加载失败：{ex.Message}";
            McpTools.Clear();
        }
    }

    private static string FormatToolingTargetName(string target)
        => string.Equals(target, "cursor", StringComparison.OrdinalIgnoreCase) ? "Cursor" :
           string.Equals(target, "codex", StringComparison.OrdinalIgnoreCase) ? "Codex" :
           string.Equals(target, "claude-code", StringComparison.OrdinalIgnoreCase) ? "Claude Code" :
           string.Equals(target, "copilot", StringComparison.OrdinalIgnoreCase) ? "GitHub Copilot" :
           string.Equals(target, "all", StringComparison.OrdinalIgnoreCase) ? "全部目标" :
           target;

    private void SetTargetStateText(string productId, string value)
    {
        if (string.Equals(productId, "cursor", StringComparison.OrdinalIgnoreCase))
            CursorStateText = value;
        else if (string.Equals(productId, "codex", StringComparison.OrdinalIgnoreCase))
            CodexStateText = value;
        else if (string.Equals(productId, "claude-code", StringComparison.OrdinalIgnoreCase))
            ClaudeCodeStateText = value;
        else if (string.Equals(productId, "copilot", StringComparison.OrdinalIgnoreCase))
            CopilotStateText = value;
    }
}

public sealed record ToolingTargetStatusViewModel(
    string ProductId,
    string DisplayName,
    string StateLabel,
    string MetaLine,
    string SummaryLine,
    bool IsInstalled);
