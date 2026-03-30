using Dna.Core.Config;
using Dna.Knowledge;

namespace Dna.Interfaces.Api;

public static class StatusEndpoints
{
    public static void MapStatusEndpoints(this IEndpointRouteBuilder app, DateTime startedAt)
    {
        app.MapGet("/api/status", (IGraphEngine graph) =>
        {
            int moduleCount = 0;
            try { moduleCount = graph.GetTopology().Nodes.Count; }
            catch { /* ignore */ }

            return Results.Json(new
            {
                moduleCount,
                startedAt,
                uptime = (DateTime.UtcNow - startedAt).ToString(@"d\.hh\:mm\:ss")
            });
        }).AllowAnonymous();
    }
}
