using Dna.ExternalAgent.Contracts;
using Dna.ExternalAgent.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Dna.ExternalAgent.Interfaces.Api;

public static class ExternalAgentEndpoints
{
    public static void MapExternalAgentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/app/tooling/list", (
            HttpRequest request,
            string? workspaceRoot,
            IExternalAgentToolingService tooling,
            IExternalAgentWorkspaceContext workspaceContext) =>
        {
            var currentWorkspace = workspaceContext.GetCurrentWorkspaceSnapshot();
            var resolvedWorkspaceRoot = ResolveWorkspaceRoot(currentWorkspace, workspaceRoot);
            var mcpEndpoint = ResolveMcpEndpoint(request);
            const string serverName = "agentic-os";

            var targetStatuses = tooling.GetTargetStatuses(resolvedWorkspaceRoot, mcpEndpoint, serverName);
            var targetViews = targetStatuses.Select(BuildTargetView).ToList();

            return Results.Ok(new ExternalAgentToolingListResponse
            {
                CurrentWorkspace = currentWorkspace,
                WorkspaceRoot = resolvedWorkspaceRoot,
                McpEndpoint = mcpEndpoint,
                ServerName = serverName,
                ReadyCount = targetViews.Count(item => item.Installed),
                PendingCount = targetViews.Count(item => !item.Installed),
                Targets = targetViews
            });
        });

        app.MapPost("/api/app/tooling/install", (
            ExternalAgentToolingInstallRequest request,
            HttpRequest httpRequest,
            IExternalAgentToolingService tooling,
            IExternalAgentWorkspaceContext workspaceContext) =>
        {
            var currentWorkspace = workspaceContext.GetCurrentWorkspaceSnapshot();
            var workspaceRoot = ResolveWorkspaceRoot(currentWorkspace, request.WorkspaceRoot);
            if (!Directory.Exists(workspaceRoot))
                return Results.BadRequest(new { error = $"workspaceRoot does not exist: {workspaceRoot}" });

            var mcpEndpoint = ResolveMcpEndpoint(httpRequest);
            var serverName = string.IsNullOrWhiteSpace(request.ServerName)
                ? "agentic-os"
                : request.ServerName.Trim();
            var replaceExisting = request.ReplaceExisting ?? true;

            var target = string.IsNullOrWhiteSpace(request.Target)
                ? "all"
                : request.Target.Trim().ToLowerInvariant();
            var knownTargets = tooling.GetTargetStatuses(workspaceRoot, mcpEndpoint, serverName)
                .Select(item => item.ProductId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (target != "all" && !knownTargets.Contains(target, StringComparer.OrdinalIgnoreCase))
                return Results.BadRequest(new { error = $"target must be one of: all, {string.Join(", ", knownTargets)}." });

            var targetIds = target == "all" ? knownTargets : [target];
            var reports = targetIds
                .Select(id => tooling.InstallTarget(id, workspaceRoot, mcpEndpoint, serverName, replaceExisting))
                .ToList();
            var targets = targetIds
                .Select(id => BuildTargetView(tooling.GetTargetStatus(id, workspaceRoot, mcpEndpoint, serverName)))
                .ToList();

            return Results.Ok(new ExternalAgentToolingInstallResponse
            {
                CurrentWorkspace = currentWorkspace,
                WorkspaceRoot = workspaceRoot,
                McpEndpoint = mcpEndpoint,
                ServerName = serverName,
                ReplaceExisting = replaceExisting,
                Reports = reports,
                Targets = targets
            });
        });

        app.MapPost("/api/app/tooling/select-folder", async (
            ExternalAgentFolderPickRequest request,
            IExternalAgentFolderPicker folderPicker,
            IExternalAgentWorkspaceContext workspaceContext,
            CancellationToken cancellationToken) =>
        {
            var defaultWorkspaceRoot = ResolveWorkspaceRoot(
                workspaceContext.GetCurrentWorkspaceSnapshot(),
                request.DefaultWorkspaceRoot);
            var selected = await folderPicker.PickFolderAsync(defaultWorkspaceRoot, request.Prompt, cancellationToken);
            return Results.Ok(new ExternalAgentFolderPickResponse
            {
                Selected = !string.IsNullOrWhiteSpace(selected),
                WorkspaceRoot = string.IsNullOrWhiteSpace(selected) ? null : selected
            });
        });

        app.MapGet("/api/app/mcp/tools", (HttpRequest request, IExternalAgentToolingService tooling) =>
        {
            var mcpEndpoint = ResolveMcpEndpoint(request);
            var tools = tooling.ListMcpTools();
            var groups = tools
                .GroupBy(item => item.Group)
                .Select(group => new
                {
                    group = group.Key,
                    count = group.Count()
                })
                .OrderBy(item => item.group, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return Results.Ok(new ExternalAgentMcpToolCatalogResponse
            {
                McpEndpoint = mcpEndpoint,
                Total = tools.Count,
                Groups = groups.Select(group => new ExternalAgentMcpToolGroup
                {
                    Group = group.group,
                    Count = group.count
                }).ToList(),
                Tools = tools
            });
        });
    }

    private static string ResolveMcpEndpoint(HttpRequest request)
        => $"{request.Scheme}://{request.Host}/mcp";

    private static string ResolveWorkspaceRoot(
        ExternalAgentWorkspaceContextSnapshot workspace,
        string? requestWorkspaceRoot = null)
    {
        if (!string.IsNullOrWhiteSpace(requestWorkspaceRoot))
            return Path.GetFullPath(requestWorkspaceRoot);

        return workspace.WorkspaceRoot;
    }

    private static ExternalAgentToolingTargetView BuildTargetView(ExternalAgentToolingTargetStatus status)
    {
        var managedFilesReady = status.ManagedFiles.Count > 0 && status.ManagedFiles.All(file => file.Exists);
        var stateLabel = status.Installed
            ? "已安装"
            : managedFilesReady && status.Integration.RequiresMcp && !status.McpConfigured
                ? "需 MCP"
                : managedFilesReady && status.Integration.Configured
                    ? "已配置"
                    : managedFilesReady
                        ? "文件已写入"
                        : "未安装";

        var badgeParts = new List<string>
        {
            $"mode={status.InstallMode}",
            $"integration={status.Integration.Kind}"
        };
        if (status.Integration.RequiresMcp)
            badgeParts.Add(status.McpConfigured ? "mcp=ok" : "mcp=pending");

        return new ExternalAgentToolingTargetView
        {
            ProductId = status.ProductId,
            DisplayName = status.DisplayName,
            Description = status.Description,
            InstallMode = status.InstallMode,
            Installed = status.Installed,
            McpConfigured = status.McpConfigured,
            Integration = status.Integration,
            ManagedFiles = status.ManagedFiles,
            StateLabel = stateLabel,
            MetaLine = string.Join(" | ", badgeParts),
            SummaryLine = status.Integration.Summary
        };
    }
}

public sealed class ExternalAgentToolingInstallRequest
{
    public string? Target { get; init; }
    public bool? ReplaceExisting { get; init; }
    public string? ServerName { get; init; }
    public string? WorkspaceRoot { get; init; }
}

public sealed class ExternalAgentFolderPickRequest
{
    public string? DefaultWorkspaceRoot { get; init; }
    public string? Prompt { get; init; }
}

public sealed class ExternalAgentToolingListResponse
{
    public ExternalAgentWorkspaceContextSnapshot CurrentWorkspace { get; init; } = new();
    public string WorkspaceRoot { get; init; } = string.Empty;
    public string McpEndpoint { get; init; } = string.Empty;
    public string ServerName { get; init; } = string.Empty;
    public int ReadyCount { get; init; }
    public int PendingCount { get; init; }
    public List<ExternalAgentToolingTargetView> Targets { get; init; } = [];
}

public sealed class ExternalAgentToolingInstallResponse
{
    public ExternalAgentWorkspaceContextSnapshot CurrentWorkspace { get; init; } = new();
    public string WorkspaceRoot { get; init; } = string.Empty;
    public string McpEndpoint { get; init; } = string.Empty;
    public string ServerName { get; init; } = string.Empty;
    public bool ReplaceExisting { get; init; }
    public List<ExternalAgentToolingInstallReport> Reports { get; init; } = [];
    public List<ExternalAgentToolingTargetView> Targets { get; init; } = [];
}

public sealed class ExternalAgentToolingTargetView
{
    public string ProductId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string InstallMode { get; init; } = string.Empty;
    public bool Installed { get; init; }
    public bool McpConfigured { get; init; }
    public ExternalAgentIntegrationStatus Integration { get; init; } = new();
    public IReadOnlyList<ExternalAgentManagedFileStatus> ManagedFiles { get; init; } = [];
    public string StateLabel { get; init; } = string.Empty;
    public string MetaLine { get; init; } = string.Empty;
    public string SummaryLine { get; init; } = string.Empty;
}

public sealed class ExternalAgentFolderPickResponse
{
    public bool Selected { get; init; }
    public string? WorkspaceRoot { get; init; }
}

public sealed class ExternalAgentMcpToolCatalogResponse
{
    public string McpEndpoint { get; init; } = string.Empty;
    public int Total { get; init; }
    public List<ExternalAgentMcpToolGroup> Groups { get; init; } = [];
    public IReadOnlyList<Dna.Workbench.Tooling.WorkbenchToolDescriptor> Tools { get; init; } = [];
}

public sealed class ExternalAgentMcpToolGroup
{
    public string Group { get; init; } = string.Empty;
    public int Count { get; init; }
}
