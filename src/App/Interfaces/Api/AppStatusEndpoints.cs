using Dna.App.Services;
using Dna.Knowledge;
using Microsoft.AspNetCore.Mvc;

namespace Dna.App.Interfaces.Api;

public static class AppStatusEndpoints
{
    public static void MapAppStatusEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/app/status", (
            [FromServices] AppRuntimeOptions options,
            [FromServices] AppWorkspaceStore workspaces,
            [FromServices] AppProjectLlmConfigService llm,
            [FromServices] ITopoGraphApplicationService topologyService,
            [FromServices] IMemoryEngine memory) =>
        {
            var currentWorkspace = workspaces.GetCurrentWorkspace();
            var topology = topologyService.GetTopology();
            var stats = memory.GetMemoryStats();

            return Results.Ok(new
            {
                app = "ok",
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
