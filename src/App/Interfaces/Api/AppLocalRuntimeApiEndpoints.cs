using Dna.App.Services;
using Dna.Core.Config;
using Dna.Knowledge;
using Microsoft.AspNetCore.Mvc;

namespace Dna.App.Interfaces.Api;

public static class AppLocalRuntimeApiEndpoints
{
    public static void MapAppLocalRuntimeApiEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/status", (
            [FromServices] ITopoGraphApplicationService topology,
            [FromServices] IMemoryEngine memory,
            [FromServices] ProjectConfig config,
            [FromServices] AppProjectLlmConfigService llm,
            [FromServices] AppRuntimeOptions runtime) =>
        {
            var moduleCount = 0;
            try
            {
                moduleCount = topology.BuildTopology().Nodes.Count;
            }
            catch
            {
                // Ignore topology failures for lightweight status.
            }

            return Results.Json(new
            {
                serviceName = "Agentic OS Runtime",
                configured = config.HasProject,
                projectRoot = config.DefaultProjectRoot,
                storePath = config.MetadataRootPath,
                dataPath = config.MetadataRootPath,
                metadataRootPath = config.MetadataRootPath,
                memoryStorePath = config.MemoryStorePath,
                sessionStorePath = config.SessionStorePath,
                knowledgeStorePath = config.KnowledgeStorePath,
                projectName = runtime.ProjectName,
                moduleCount,
                memoryCount = memory.MemoryCount(),
                startedAt = runtime.StartedAtUtc,
                uptime = (DateTime.UtcNow - runtime.StartedAtUtc).ToString(@"d\.hh\:mm\:ss"),
                transport = "Local REST + MCP",
                productMode = "single-user-local-app",
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
