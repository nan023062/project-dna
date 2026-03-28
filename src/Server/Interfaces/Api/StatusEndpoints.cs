using Dna.Core.Config;
using Dna.Knowledge;

namespace Dna.Interfaces.Api;

public static class StatusEndpoints
{
    public static void MapStatusEndpoints(this IEndpointRouteBuilder app, DateTime startedAt)
    {
        app.MapGet("/api/status", (ProjectConfig config, IGraphEngine graph) =>
        {
            var root = config.DefaultProjectRoot;
            var hasProject = !string.IsNullOrEmpty(root);
            int moduleCount = 0;
            if (hasProject)
            {
                try
                {
                    graph.Initialize(root!);
                    moduleCount = graph.BuildTopology().Nodes.Count;
                }
                catch { /* ignore */ }
            }

            return Results.Json(new
            {
                projectRoot = root,
                configured = hasProject,
                moduleCount,
                startedAt,
                uptime = (DateTime.UtcNow - startedAt).ToString(@"d\.hh\:mm\:ss")
            });
        });
    }
}
