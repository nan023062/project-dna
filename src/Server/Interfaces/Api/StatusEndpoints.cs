using Dna.Auth;
using Dna.Core.Config;
using Dna.Knowledge;
using Dna.Services;

namespace Dna.Interfaces.Api;

public static class StatusEndpoints
{
    public static void MapStatusEndpoints(this IEndpointRouteBuilder app, DateTime startedAt)
    {
        app.MapGet("/api/status", (
            IGraphEngine graph,
            ProjectConfig config,
            ServerRuntimeOptions runtimeOptions,
            ServerRuntimeLlmConfigService runtimeLlm,
            ServerAllowlistStore allowlist,
            UserStore users) =>
        {
            var moduleCount = 0;
            try
            {
                moduleCount = graph.GetTopology().Nodes.Count;
            }
            catch
            {
                // Ignore topology read failures on the lightweight status endpoint.
            }

            var allowlistSnapshot = allowlist.GetSnapshot();
            var allowlistEntries = allowlistSnapshot.Entries;
            var userEntries = users.ListUsers();

            return Results.Json(new
            {
                serviceName = "Project DNA Server",
                configured = config.HasProject,
                projectRoot = config.DefaultProjectRoot,
                storePath = config.DnaStorePath,
                dataPath = runtimeOptions.DataPath,
                projectName = ResolveProjectName(config.DefaultProjectRoot),
                moduleCount,
                startedAt,
                uptime = (DateTime.UtcNow - startedAt).ToString(@"d\.hh\:mm\:ss"),
                transport = "REST + JWT + Allowlist + SSE",
                productMode = "single-user-admin-mvp",
                runtimeLlm = runtimeLlm.GetStatusSummary(),
                allowlist = new
                {
                    total = allowlistEntries.Count,
                    enabled = allowlistEntries.Count(entry => entry.Enabled),
                    admin = allowlistEntries.Count(entry =>
                        entry.Enabled && string.Equals(entry.Role, ServerRoles.Admin, StringComparison.OrdinalIgnoreCase)),
                    updatedAtUtc = allowlistSnapshot.UpdatedAtUtc
                },
                users = new
                {
                    total = userEntries.Count,
                    admin = userEntries.Count(user => string.Equals(user.Role, ServerRoles.Admin, StringComparison.OrdinalIgnoreCase)),
                    editor = userEntries.Count(user => string.Equals(user.Role, ServerRoles.Editor, StringComparison.OrdinalIgnoreCase)),
                    viewer = userEntries.Count(user => string.Equals(user.Role, ServerRoles.Viewer, StringComparison.OrdinalIgnoreCase))
                }
            });
        }).AllowAnonymous();
    }

    private static string ResolveProjectName(string? projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
            return "未配置项目";

        var normalized = projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Path.GetFileName(normalized);
    }
}
