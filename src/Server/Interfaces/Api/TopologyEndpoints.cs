using System.Text.Json;
using System.Text.Json.Serialization;
using Dna.Core.Config;
using Dna.Knowledge;
using Microsoft.AspNetCore.Mvc;

namespace Dna.Interfaces.Api;

public static class TopologyEndpoints
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static void MapTopologyEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/topology", (IGraphEngine graph, ProjectConfig config) =>
        {
            var topo = graph.BuildTopology();

            return Results.Json(new
            {
                projectRoot = config.DefaultProjectRoot,
                modules = topo.Nodes.Select(m =>
                {
                    var nameCount = topo.Nodes.Count(o =>
                        o.Name.Equals(m.Name, StringComparison.OrdinalIgnoreCase));
                    var id = nameCount > 1 ? (m.RelativePath ?? m.Name).Replace('\\', '/') : m.Name;
                    return new
                    {
                        name = id,
                        displayName = m.Name,
                        relativePath = m.RelativePath,
                        discipline = m.Discipline,
                        type = m.Type.ToString(),
                        m.Summary,
                        m.Keywords,
                        dependencies = m.Dependencies,
                        computedDependencies = m.ComputedDependencies,
                        m.Maintainer
                    };
                }),
                edges = topo.Edges.Select(e => new
                {
                    from = e.From,
                    to = e.To,
                    isComputed = e.IsComputed
                }),
                depMap = topo.DepMap,
                rdepMap = topo.RdepMap,
                summary = $"共 {topo.Nodes.Count} 个模块，{topo.Edges.Count} 条依赖边 · {topo.BuiltAt:yyyy-MM-dd HH:mm}",
                scannedAt = topo.BuiltAt
            });
        });

        api.MapGet("/plan", (
            [FromQuery] string modules,
            IGraphEngine graph) =>
        {
            graph.BuildTopology();
            var names = modules
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
            var plan = graph.GetExecutionPlan(names);
            return Results.Json(new
            {
                plan.OrderedModules,
                plan.HasCycle,
                plan.CycleDescription,
                executionOrder = string.Join(" → ", plan.OrderedModules)
            }, JsonOpts);
        });

        api.MapPost("/reload", (IGraphEngine graph) =>
        {
            graph.ReloadManifests();
            var topo = graph.BuildTopology();
            return Results.Json(new
            {
                success = true,
                message = $"已重载，{topo.Nodes.Count} 个模块",
                moduleCount = topo.Nodes.Count
            });
        });
    }
}
