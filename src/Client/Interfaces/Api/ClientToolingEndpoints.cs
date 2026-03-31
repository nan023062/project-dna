using Dna.Client.Services.Tooling;

namespace Dna.Client.Interfaces.Api;

public static class ClientToolingEndpoints
{
    public static void MapClientToolingEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/client/tooling/list", (HttpRequest request, ClientIdeToolingService tooling) =>
        {
            var workspaceRoot = ResolveWorkspaceRoot();
            var mcpEndpoint = ResolveMcpEndpoint(request);
            const string serverName = "project-dna";

            var targets = new[]
            {
                tooling.GetStatus("cursor", workspaceRoot, mcpEndpoint, serverName),
                tooling.GetStatus("codex", workspaceRoot, mcpEndpoint, serverName)
            };

            return Results.Ok(new
            {
                workspaceRoot,
                mcpEndpoint,
                serverName,
                targets
            });
        });

        app.MapPost("/api/client/tooling/install", (ClientToolingInstallRequest request, HttpRequest httpRequest, ClientIdeToolingService tooling) =>
        {
            var workspaceRoot = ResolveWorkspaceRoot(request.WorkspaceRoot);
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

    private static string ResolveWorkspaceRoot(string? requestWorkspaceRoot = null)
    {
        if (!string.IsNullOrWhiteSpace(requestWorkspaceRoot))
            return Path.GetFullPath(requestWorkspaceRoot);

        var envWorkspace = Environment.GetEnvironmentVariable("DNA_WORKSPACE_ROOT");
        if (!string.IsNullOrWhiteSpace(envWorkspace))
            return Path.GetFullPath(envWorkspace);

        var cwd = Directory.GetCurrentDirectory();
        if (!LooksLikeClientProjectDirectory(cwd))
            return cwd;

        var repoRoot = Path.GetFullPath(Path.Combine(cwd, "..", ".."));
        return Directory.Exists(repoRoot) ? repoRoot : cwd;
    }

    private static bool LooksLikeClientProjectDirectory(string path)
    {
        try
        {
            if (!File.Exists(Path.Combine(path, "Client.csproj")))
                return false;
            if (!Directory.Exists(Path.Combine(path, "wwwroot")))
                return false;

            var repoRoot = Path.GetFullPath(Path.Combine(path, "..", ".."));
            return Directory.Exists(Path.Combine(repoRoot, "src")) &&
                   Directory.Exists(Path.Combine(repoRoot, "client-tools"));
        }
        catch
        {
            return false;
        }
    }
}

public sealed class ClientToolingInstallRequest
{
    public string? Target { get; init; }
    public bool? ReplaceExisting { get; init; }
    public string? ServerName { get; init; }
    public string? WorkspaceRoot { get; init; }
}
