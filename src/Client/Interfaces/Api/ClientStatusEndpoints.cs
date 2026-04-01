using Dna.Client.Services;
using Dna.Knowledge;
using Microsoft.AspNetCore.Mvc;

namespace Dna.Client.Interfaces.Api;

public static class ClientStatusEndpoints
{
    public static void MapClientStatusEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/client/status", (
            [FromServices] ClientRuntimeOptions options,
            [FromServices] ClientWorkspaceStore workspaces,
            [FromServices] ClientProjectLlmConfigService llm,
            [FromServices] IGraphEngine graph,
            [FromServices] IMemoryEngine memory) =>
        {
            var currentWorkspace = workspaces.GetCurrentWorkspace();
            var topology = graph.GetTopology();
            var stats = memory.GetMemoryStats();

            return Results.Ok(new
            {
                client = "ok",
                apiBaseUrl = options.ApiBaseUrl,
                currentWorkspace,
                projectLlm = llm.GetSummary(),
                topology = new
                {
                    moduleCount = topology.Nodes.Count,
                    edgeCount = topology.Edges.Count,
                    crossWorkCount = topology.CrossWorks.Count
                },
                memory = new
                {
                    total = stats.Total,
                    conflicts = stats.ConflictCount
                },
                startedAt = options.StartedAtUtc,
                uptime = (DateTime.UtcNow - options.StartedAtUtc).ToString(@"d\.hh\:mm\:ss")
            });
        });
    }
}
