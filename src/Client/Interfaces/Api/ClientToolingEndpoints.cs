using Dna.Client.Services;
using Dna.Client.Services.Tooling;

namespace Dna.Client.Interfaces.Api;

public static class ClientToolingEndpoints
{
    public static void MapClientToolingEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/client/tooling/list", (HttpRequest request, ClientIdeToolingService tooling, ClientWorkspaceStore workspaces) =>
        {
            var currentWorkspace = workspaces.GetCurrentWorkspace();
            var workspaceRoot = ResolveWorkspaceRoot(workspaces);
            var mcpEndpoint = ResolveMcpEndpoint(request);
            const string serverName = "project-dna";

            var targets = new[]
            {
                tooling.GetStatus("cursor", workspaceRoot, mcpEndpoint, serverName),
                tooling.GetStatus("codex", workspaceRoot, mcpEndpoint, serverName)
            };

            return Results.Ok(new
            {
                currentWorkspace,
                workspaceRoot,
                mcpEndpoint,
                serverName,
                targets
            });
        });

        app.MapPost("/api/client/tooling/install", (ClientToolingInstallRequest request, HttpRequest httpRequest, ClientIdeToolingService tooling, ClientWorkspaceStore workspaces) =>
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
                ? "project-dna"
                : request.ServerName.Trim();
            var replaceExisting = request.ReplaceExisting ?? true;

            var target = string.IsNullOrWhiteSpace(request.Target)
                ? "all"
                : request.Target.Trim().ToLowerInvariant();
            if (target is not ("all" or "cursor" or "codex"))
            {
                return Results.BadRequest(new
                {
                    error = "target must be one of: all, cursor, codex."
                });
            }

            var targetIds = target == "all"
                ? new[] { "cursor", "codex" }
                : new[] { target };

            var reports = targetIds
                .Select(id => tooling.InstallTarget(id, workspaceRoot, mcpEndpoint, serverName, replaceExisting))
                .ToList();
            var targets = targetIds
                .Select(id => tooling.GetStatus(id, workspaceRoot, mcpEndpoint, serverName))
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
    }

    private static string ResolveMcpEndpoint(HttpRequest request)
        => $"{request.Scheme}://{request.Host}/mcp";

    private static string ResolveWorkspaceRoot(ClientWorkspaceStore workspaces, string? requestWorkspaceRoot = null)
    {
        if (!string.IsNullOrWhiteSpace(requestWorkspaceRoot))
            return Path.GetFullPath(requestWorkspaceRoot);

        return workspaces.GetCurrentWorkspace().WorkspaceRoot;
    }
}

public sealed class ClientToolingInstallRequest
{
    public string? Target { get; init; }
    public bool? ReplaceExisting { get; init; }
    public string? ServerName { get; init; }
    public string? WorkspaceRoot { get; init; }
}
