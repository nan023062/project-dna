using System.Text.Json;
using System.Text.Json.Serialization;
using Dna.Auth;
using Dna.Knowledge;
using Microsoft.AspNetCore.Mvc;

namespace Dna.Interfaces.Api;

public static class GraphEndpoints
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static void MapGraphEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/graph");
        api.RequireAuthorization(ServerPolicies.ViewerOrAbove);

        api.MapGet("/identity", (IGraphEngine graph) =>
        {
            var topo = graph.GetTopology();
            return Results.Json(new
            {
                moduleCount = topo.Nodes.Count,
                edgeCount = topo.Edges.Count,
                crossWorkCount = topo.CrossWorks.Count
            }, JsonOpts);
        });

        api.MapGet("/search", (
            [FromQuery] string q,
            [FromQuery] int maxResults,
            IGraphEngine graph) =>
        {
            if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
                return Results.Json(new { error = "query 至少 2 个字符" }, statusCode: 400);

            graph.BuildTopology();

            var query = q.Trim().ToLowerInvariant();
            var limit = Math.Clamp(maxResults > 0 ? maxResults : 8, 1, 20);

            var results = graph.GetAllModules()
                .Select(m => new { Module = m, Score = ScoreModule(m, query) })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Module.Name)
                .Take(limit)
                .Select(x => new
                {
                    name = x.Module.Name,
                    discipline = x.Module.Discipline,
                    relativePath = x.Module.RelativePath,
                    summary = x.Module.Summary,
                    type = x.Module.Type.ToString(),
                    score = Math.Round(x.Score, 3)
                })
                .ToList();

            return Results.Json(new { query = q, count = results.Count, results }, JsonOpts);
        });

        api.MapGet("/context", (
            [FromQuery] string target,
            [FromQuery] string? current,
            [FromQuery] string? activeModules,
            IGraphEngine graph) =>
        {
            if (string.IsNullOrWhiteSpace(target))
                return Results.Json(new { error = "target 不能为空" }, statusCode: 400);

            graph.BuildTopology();

            var active = string.IsNullOrWhiteSpace(activeModules) ? null
                : activeModules.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            var cur = string.IsNullOrWhiteSpace(current) ? target : current;

            var context = graph.GetModuleContext(target, cur, active);
            var crossWorks = graph.GetCrossWorksForModule(target);

            return Results.Json(new
            {
                target,
                context,
                crossWorks = crossWorks.Select(cw => new
                {
                    cw.Name,
                    cw.Description,
                    cw.Feature,
                    participants = cw.Participants.Select(p => new
                    {
                        p.ModuleName,
                        p.Role,
                        p.Contract,
                        p.Deliverable
                    })
                })
            }, JsonOpts);
        });

        api.MapPost("/begin-task", (
            [FromBody] BeginTaskRequest request,
            IGraphEngine graph) =>
        {
            var topo = graph.BuildTopology();

            if (request.ModuleNames == null || request.ModuleNames.Count == 0)
            {
                var modules = topo.Nodes
                    .OrderBy(n => n.Discipline ?? "generic")
                    .ThenBy(n => n.Name)
                    .Select(n => new
                    {
                        name = n.Name,
                        discipline = n.Discipline,
                        type = n.Type.ToString(),
                        summary = n.Summary
                    })
                    .ToList();

                return Results.Json(new
                {
                    moduleCount = topo.Nodes.Count,
                    edgeCount = topo.Edges.Count,
                    crossWorkCount = topo.CrossWorks.Count,
                    modules
                }, JsonOpts);
            }

            var current = request.ModuleNames[0];
            var contexts = request.ModuleNames.Select(name =>
            {
                var ctx = graph.GetModuleContext(name, current, request.ModuleNames);
                return new { module = name, context = ctx };
            }).ToList();

            var crossWorks = request.ModuleNames
                .SelectMany(n => graph.GetCrossWorksForModule(n))
                .DistinctBy(cw => cw.Name)
                .Select(cw => new
                {
                    cw.Name,
                    cw.Description,
                    cw.Feature,
                    participants = cw.Participants.Select(p => new
                    {
                        p.ModuleName,
                        p.Role,
                        p.Contract,
                        p.Deliverable
                    })
                })
                .ToList();

            return Results.Json(new { contexts, crossWorks }, JsonOpts);
        });
    }

    private static double ScoreModule(KnowledgeNode module, string q)
    {
        var score = 0d;
        if (module.Name.Contains(q, StringComparison.OrdinalIgnoreCase)) score += 3.0;
        if (module.RelativePath?.Contains(q, StringComparison.OrdinalIgnoreCase) == true) score += 2.0;
        if (!string.IsNullOrWhiteSpace(module.Summary) &&
            module.Summary.Contains(q, StringComparison.OrdinalIgnoreCase)) score += 1.5;
        if (module.Keywords.Any(k => k.Contains(q, StringComparison.OrdinalIgnoreCase))) score += 1.2;
        if (module.Dependencies.Any(d => d.Contains(q, StringComparison.OrdinalIgnoreCase))) score += 0.5;
        return score;
    }
}

public class BeginTaskRequest
{
    public List<string>? ModuleNames { get; set; }
}
