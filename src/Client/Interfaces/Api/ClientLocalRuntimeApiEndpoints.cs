using Dna.Client.Services;
using Dna.Core.Config;
using Dna.Knowledge;
using Microsoft.AspNetCore.Mvc;

namespace Dna.Client.Interfaces.Api;

public static class ClientLocalRuntimeApiEndpoints
{
    public static void MapClientLocalRuntimeApiEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/status", (
            [FromServices] IGraphEngine graph,
            [FromServices] IMemoryEngine memory,
            [FromServices] ProjectConfig config,
            [FromServices] ClientProjectLlmConfigService llm,
            [FromServices] ClientRuntimeOptions runtime) =>
        {
            var moduleCount = 0;
            try
            {
                moduleCount = graph.BuildTopology().Nodes.Count;
            }
            catch
            {
                // Ignore topology failures for lightweight status.
            }

            return Results.Json(new
            {
                serviceName = "Agentic OS Client Runtime",
                configured = config.HasProject,
                projectRoot = config.DefaultProjectRoot,
                storePath = config.DnaStorePath,
                dataPath = config.DnaStorePath,
                projectName = runtime.ProjectName,
                moduleCount,
                memoryCount = memory.MemoryCount(),
                startedAt = runtime.StartedAtUtc,
                uptime = (DateTime.UtcNow - runtime.StartedAtUtc).ToString(@"d\.hh\:mm\:ss"),
                transport = "Local REST + MCP",
                productMode = "single-user-local-client",
                runtimeLlm = llm.GetSummary()
            });
        });

        app.MapGet("/api/connection/access", (HttpContext context) =>
        {
            var remoteIp = context.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
            return Results.Ok(new
            {
                allowed = true,
                role = "admin",
                entryName = "local-runtime",
                remoteIp,
                note = "single-process desktop runtime",
                reason = string.Empty
            });
        });
    }
}
