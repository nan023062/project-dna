using System.Text.Json;
using System.Text.Json.Serialization;
using Dna.Core.Config;
using Dna.Knowledge;
using Microsoft.AspNetCore.Mvc;

namespace Dna.App.Interfaces.Api;

public static class AppLocalTopologyApiEndpoints
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static void MapAppLocalTopologyApiEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/topology", ([FromServices] ITopoGraphApplicationService topology) =>
        {
            topology.BuildTopology();
            return Results.Json(topology.GetWorkbenchSnapshot(), JsonOpts);
        });

        api.MapGet("/mcdp", ([FromServices] ITopoGraphApplicationService topology, [FromServices] ProjectConfig config) =>
        {
            topology.BuildTopology();
            return Results.Json(topology.GetMcdpProjection(config.DefaultProjectRoot), JsonOpts);
        });

        api.MapGet("/plan", ([FromQuery] string modules, [FromServices] ITopoGraphApplicationService topology) =>
        {
            topology.BuildTopology();
            var names = modules
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
            var plan = topology.GetExecutionPlan(names);
            return Results.Json(new
            {
                plan.OrderedModules,
                plan.HasCycle,
                plan.CycleDescription,
                executionOrder = string.Join(" -> ", plan.OrderedModules)
            }, JsonOpts);
        });

        api.MapPost("/reload", ([FromServices] ITopoGraphApplicationService topology) =>
        {
            topology.ReloadManifests();
            var topo = topology.BuildTopology();
            return Results.Json(new
            {
                success = true,
                message = $"Reloaded {topo.Nodes.Count} nodes",
                moduleCount = topo.Nodes.Count
            });
        });
    }
}
