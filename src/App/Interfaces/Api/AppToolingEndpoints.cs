using Dna.App.Services;
using Dna.App.Services.Tooling;
using Dna.ExternalAgent.Contracts;

namespace Dna.App.Interfaces.Api;

public static class AppToolingEndpoints
{
    public static void MapAppToolingEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/app/tooling/list", (HttpRequest request, string? workspaceRoot, IExternalAgentToolingService tooling, AppWorkspaceStore workspaces) =>
        {
            var currentWorkspace = workspaces.GetCurrentWorkspace();
            var resolvedWorkspaceRoot = ResolveWorkspaceRoot(workspaces, workspaceRoot);
            var mcpEndpoint = ResolveMcpEndpoint(request);
            const string serverName = "agentic-os";

            var targets = tooling.GetTargetStatuses(resolvedWorkspaceRoot, mcpEndpoint, serverName);

            return Results.Ok(new
            {
                currentWorkspace,
                workspaceRoot = resolvedWorkspaceRoot,
                mcpEndpoint,
                serverName,
                targets
            });
        });

        app.MapPost("/api/app/tooling/install", (AppToolingInstallRequest request, HttpRequest httpRequest, IExternalAgentToolingService tooling, AppWorkspaceStore workspaces) =>
        {
            var currentWorkspace = workspaces.GetCurrentWorkspace();
            var workspaceRoot = ResolveWorkspaceRoot(workspaces, request.WorkspaceRoot);
            if (!Directory.Exists(workspaceRoot))
            {
                return Results.BadRequest(new
                {
                    error = $"workspaceRoot does not exist: {workspaceRoot}"
                });
            }

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
                .Select(id => tooling.GetTargetStatus(id, workspaceRoot, mcpEndpoint, serverName))
                .ToList();

            return Results.Ok(new
            {
                currentWorkspace,
                workspaceRoot,
                mcpEndpoint,
                serverName,
                replaceExisting,
                reports,
                targets
            });
        });

        app.MapPost("/api/app/tooling/select-folder", async (
            AppToolingFolderPickRequest request,
            AppFolderPickerService folderPicker,
            AppWorkspaceStore workspaces,
            CancellationToken cancellationToken) =>
        {
            var defaultWorkspaceRoot = ResolveWorkspaceRoot(workspaces, request.DefaultWorkspaceRoot);
            var selected = await folderPicker.PickFolderAsync(defaultWorkspaceRoot, request.Prompt, cancellationToken);
            return Results.Ok(new
            {
                selected = !string.IsNullOrWhiteSpace(selected),
                workspaceRoot = string.IsNullOrWhiteSpace(selected) ? null : selected
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

            return Results.Ok(new
            {
                mcpEndpoint,
                total = tools.Count,
                groups,
                tools
            });
        });
    }

    private static string ResolveMcpEndpoint(HttpRequest request)
        => $"{request.Scheme}://{request.Host}/mcp";

    private static string ResolveWorkspaceRoot(AppWorkspaceStore workspaces, string? requestWorkspaceRoot = null)
    {
        if (!string.IsNullOrWhiteSpace(requestWorkspaceRoot))
            return Path.GetFullPath(requestWorkspaceRoot);

        return workspaces.GetCurrentWorkspace().WorkspaceRoot;
    }
}

public sealed class AppToolingInstallRequest
{
    public string? Target { get; init; }
    public bool? ReplaceExisting { get; init; }
    public string? ServerName { get; init; }
    public string? WorkspaceRoot { get; init; }
}

public sealed class AppToolingFolderPickRequest
{
    public string? DefaultWorkspaceRoot { get; init; }
    public string? Prompt { get; init; }
}
