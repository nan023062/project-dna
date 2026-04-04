using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dna.App.Desktop.Services;

using Dna.App.Services;

namespace Dna.App.Desktop.ViewModels;

public partial class ToolingViewModel : ObservableObject
{
    private readonly IDnaApiClient _apiClient;

    [ObservableProperty]
    private string _cursorStateText = "Cursor: -";

    [ObservableProperty]
    private string _codexStateText = "Codex: -";

    [ObservableProperty]
    private string _toolingHubText = "App 保留本地 5052 运行时，既服务桌面宿主内部调用，也向外暴露 CLI 与 /mcp。";

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
            ToolingHubText = "请先加载项目工作区，再为 IDE 安装本地 DNA App 连接配置。";
            ToolingStatusText = "请先加载项目工作区。";
            return;
        }

        try
        {
            var tooling = await _apiClient.GetAsync("/api/app/tooling/list");
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
            CursorStateText = cursorInstalled ? "Cursor: 已安装" : "Cursor: 未安装";
            CodexStateText = codexInstalled ? "Codex: 已安装" : "Codex: 未安装";
            ToolingHubText = $"本地 5052 运行时已就绪。当前工作区：{workspaceRoot}；CLI：agentic-os cli；MCP 入口：{AppRuntimeConstants.ApiBaseUrl}/mcp";
            ToolingStatusText = $"工具状态已刷新。工作区：{workspaceRoot}";
        }
        catch (Exception ex)
        {
            CursorStateText = "Cursor: 错误";
            CodexStateText = "Codex: 错误";
            ToolingStatusText = $"工具状态刷新失败：{ex.Message}";
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
            var picked = await _apiClient.PostAsync("/api/app/tooling/select-folder", new
            {
                defaultWorkspaceRoot = ProjectRoot,
                prompt = $"选择要安装 {FormatToolingTargetName(target)} 工作流配置的项目目录"
            });

            var accepted = picked.TryGetProperty("selected", out var selectedElement) &&
                           selectedElement.ValueKind == JsonValueKind.True;
            if (!accepted)
            {
                ToolingStatusText = "已取消目录选择。";
                return;
            }

            var workspaceRoot = GetString(picked, "workspaceRoot", string.Empty);
            if (string.IsNullOrWhiteSpace(workspaceRoot))
                throw new InvalidOperationException("未获取到有效工作区目录。");

            var install = await _apiClient.PostAsync("/api/app/tooling/install", new
            {
                target,
                replaceExisting = true,
                workspaceRoot
            });

            var reportsCount = install.TryGetProperty("reports", out var reports) && reports.ValueKind == JsonValueKind.Array
                ? reports.GetArrayLength()
                : 0;

            ToolingStatusText = $"{FormatToolingTargetName(target)} 安装完成（报告 {reportsCount} 条），目录：{workspaceRoot}";
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
            var catalog = await _apiClient.GetAsync("/api/app/mcp/tools");
            var endpoint = GetString(catalog, "mcpEndpoint", GetString(catalog, "endpoint", $"{AppRuntimeConstants.ApiBaseUrl}/mcp"));

            McpTools.Clear();
            if (catalog.TryGetProperty("tools", out var tools) && tools.ValueKind == JsonValueKind.Array)
            {
                foreach (var tool in tools.EnumerateArray())
                {
                    var name = GetString(tool, "name", "-");
                    var group = GetString(tool, "group", "General");
                    var description = GetString(tool, "description", string.Empty);
                    McpTools.Add($"[{group}] {name} - {description}");
                }
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
           string.Equals(target, "codex", StringComparison.OrdinalIgnoreCase) ? "Codex" : target;

    private static string? GetString(JsonElement element, string propertyName, string? defaultValue)
    {
        if (element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String)
            return value.GetString();
        return defaultValue;
    }
}
