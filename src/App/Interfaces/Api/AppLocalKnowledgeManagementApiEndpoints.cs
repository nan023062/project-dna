using System.Text.Json;
using System.Text.Json.Serialization;
using Dna.Core.Config;
using Dna.Knowledge;
using Dna.Memory.Models;
using Dna.Knowledge.Workspace.Models;
using Microsoft.AspNetCore.Mvc;

namespace Dna.App.Interfaces.Api;

public static class AppLocalKnowledgeManagementApiEndpoints
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static void MapAppLocalKnowledgeManagementApiEndpoints(this IEndpointRouteBuilder app)
    {
        MapGraphEndpoints(app);
        MapGovernanceEndpoints(app);
        MapModuleManagementEndpoints(app);
    }

    private static void MapGraphEndpoints(IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/graph");

        api.MapGet("/identity", ([FromServices] ITopoGraphApplicationService topology) =>
        {
            var topo = topology.GetTopology();
            return Results.Json(new
            {
                moduleCount = topo.Nodes.Count,
                edgeCount = topo.Edges.Count,
                crossWorkCount = topo.CrossWorks.Count
            }, JsonOpts);
        });

        api.MapGet("/search", ([FromQuery] string q, [FromQuery] int maxResults, [FromServices] ITopoGraphApplicationService topology) =>
        {
            if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
                return Results.Json(new { error = "query must be at least 2 characters." }, statusCode: 400);

            topology.BuildTopology();
            var query = q.Trim().ToLowerInvariant();
            var limit = Math.Clamp(maxResults > 0 ? maxResults : 8, 1, 20);

            var results = topology.GetAllModules()
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

        api.MapGet("/context", ([FromQuery] string target, [FromQuery] string? current, [FromQuery] string? activeModules, [FromServices] ITopoGraphApplicationService topology, [FromServices] IMemoryEngine memory) =>
        {
            if (string.IsNullOrWhiteSpace(target))
                return Results.Json(new { error = "target is required." }, statusCode: 400);

            topology.BuildTopology();
            var active = string.IsNullOrWhiteSpace(activeModules)
                ? null
                : activeModules.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            var currentModule = string.IsNullOrWhiteSpace(current) ? target : current;
            var context = topology.GetModuleContext(target, currentModule, active);
            var crossWorks = topology.GetCrossWorksForModule(target);
            var session = BuildSessionSnapshot(topology, memory, target);

            return Results.Json(new
            {
                target,
                context,
                session,
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

        api.MapPost("/begin-task", ([FromBody] BeginTaskRequest request, [FromServices] ITopoGraphApplicationService topology, [FromServices] IMemoryEngine memory) =>
        {
            var topo = topology.BuildTopology();

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
                var context = topology.GetModuleContext(name, current, request.ModuleNames);
                var session = BuildSessionSnapshot(topology, memory, name);
                return new { module = name, context, session };
            }).ToList();

            var crossWorks = request.ModuleNames
                .SelectMany(topology.GetCrossWorksForModule)
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

    private static object BuildSessionSnapshot(
        ITopoGraphApplicationService topology,
        IMemoryEngine memory,
        string moduleName)
    {
        var module = topology.FindModule(moduleName);
        if (module?.Id == null)
            return new { count = 0, items = Array.Empty<object>() };

        var items = memory.QueryMemories(new MemoryFilter
            {
                NodeId = module.Id,
                Stages = [MemoryStage.ShortTerm],
                Freshness = FreshnessFilter.FreshAndAging,
                Limit = 10
            })
            .OrderByDescending(entry => entry.CreatedAt)
            .Select(entry => new
            {
                entry.Id,
                type = entry.Type.ToString(),
                stage = entry.Stage.ToString(),
                entry.Summary,
                entry.Content,
                entry.Tags,
                entry.CreatedAt
            })
            .ToList();

        return new
        {
            count = items.Count,
            items
        };
    }

    private static void MapGovernanceEndpoints(IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/governance");

        api.MapGet("/validate", ([FromServices] IGovernanceEngine governance) =>
        {
            var report = governance.ValidateArchitecture();
            return Results.Json(new
            {
                healthy = report.IsHealthy,
                totalIssues = report.TotalIssues,
                cycleSuggestions = report.CycleSuggestions.Select(c => new { c.Message, c.Suggestion }),
                orphanNodes = report.OrphanNodes.Select(n => new { n.Name, n.Discipline, type = n.Type.ToString() }),
                crossWorkIssues = report.CrossWorkIssues.Select(i => new { i.CrossWorkName, i.Message }),
                dependencyDrifts = report.DependencyDrifts.Select(d => new { d.ModuleName, d.Message, d.Suggestion }),
                keyNodeWarnings = report.KeyNodeWarnings.Select(w => new { w.NodeName, w.Message })
            }, JsonOpts);
        });

        api.MapGet("/freshness", ([FromServices] IGovernanceEngine governance) =>
        {
            var decayed = governance.CheckFreshness();
            return Results.Json(new
            {
                decayedCount = decayed,
                message = decayed > 0 ? $"Decayed {decayed} stale memories." : "All memories are fresh enough."
            }, JsonOpts);
        });

        api.MapPost("/evolve", async ([FromBody] EvolveKnowledgeRequest request, [FromServices] IGovernanceEngine governance) =>
        {
            var result = await governance.EvolveKnowledgeAsync(
                string.IsNullOrWhiteSpace(request.NodeIdOrName) ? null : request.NodeIdOrName,
                request.MaxSuggestions is > 0 ? request.MaxSuggestions.Value : 50);
            return Results.Json(result, JsonOpts);
        });

        api.MapPost("/condense/node", async ([FromBody] CondenseNodeRequest request, [FromServices] IGovernanceEngine governance) =>
        {
            if (string.IsNullOrWhiteSpace(request.NodeIdOrName))
                return Results.BadRequest(new { error = "nodeIdOrName is required." });

            var result = await governance.CondenseNodeKnowledgeAsync(
                request.NodeIdOrName,
                request.MaxSourceMemories is > 0 ? request.MaxSourceMemories.Value : 200);
            return Results.Json(result, JsonOpts);
        });

        api.MapPost("/condense/all", async ([FromBody] CondenseAllRequest request, [FromServices] IGovernanceEngine governance) =>
        {
            var results = await governance.CondenseAllNodesAsync(
                request.MaxSourceMemories is > 0 ? request.MaxSourceMemories.Value : 200);

            return Results.Json(new
            {
                total = results.Count,
                condensed = results.Count(r => r.NewIdentityMemoryId != null),
                archived = results.Sum(r => r.ArchivedCount),
                results
            }, JsonOpts);
        });
    }

    private static void MapModuleManagementEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/modules");

        group.MapGet("/manifest", ([FromServices] ITopoGraphApplicationService topology, [FromServices] ProjectConfig config) =>
        {
            EnsureReady(topology, config);
            var snapshot = topology.GetManagementSnapshot();
            var merged = snapshot.Disciplines.ToDictionary(
                item => item.Id,
                item => new
                {
                    displayName = item.DisplayName,
                    roleId = item.RoleId,
                    layers = item.Layers,
                    modules = snapshot.Modules
                        .Where(module => string.Equals(module.Discipline, item.Id, StringComparison.OrdinalIgnoreCase))
                        .ToList()
                });
            return Results.Ok(new
            {
                disciplines = merged,
                crossWorks = snapshot.CrossWorks,
                features = new Dictionary<string, object>()
            });
        });

        group.MapPost("/", ([FromBody] UpsertModuleRequest request, [FromServices] ITopoGraphApplicationService topology, [FromServices] ProjectConfig config) =>
        {
            EnsureReady(topology, config);
            if (string.IsNullOrWhiteSpace(request.Discipline))
                return Results.BadRequest(new { error = "discipline is required." });
            if (request.Module is null)
                return Results.BadRequest(new { error = "module is required." });
            if (string.IsNullOrWhiteSpace(request.Module.Name) || string.IsNullOrWhiteSpace(request.Module.Path))
                return Results.BadRequest(new { error = "module.name and module.path are required." });

            var discipline = request.Discipline.Trim();
            if (request.Module.IsCrossWorkModule)
            {
                var (computedDiscipline, computedLayer) = ComputeCwOwnership(request.Module.Participants, topology.GetManagementSnapshot().Modules);
                discipline = computedDiscipline;
                request.Module.Layer = computedLayer;
            }
            else
            {
                topology.UpsertDiscipline(discipline, discipline, "coder", []);
            }

            try
            {
                topology.RegisterModule(discipline, request.Module);
                topology.BuildTopology();
                return Results.Ok(new { message = "Module saved." });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        group.MapDelete("/{moduleName}", (string moduleName, [FromServices] ITopoGraphApplicationService topology, [FromServices] ProjectConfig config) =>
        {
            EnsureReady(topology, config);
            var ok = topology.UnregisterModule(moduleName);
            if (!ok) return Results.NotFound(new { error = $"Module not found: {moduleName}" });
            topology.BuildTopology();
            return Results.Ok(new { message = $"Module removed: {moduleName}" });
        });

        group.MapGet("/disciplines", ([FromServices] ITopoGraphApplicationService topology, [FromServices] ProjectConfig config) =>
        {
            EnsureReady(topology, config);
            var snapshot = topology.GetManagementSnapshot();
            var result = snapshot.Disciplines.Select(item => new
            {
                id = item.Id,
                displayName = item.DisplayName,
                roleId = item.RoleId,
                layers = item.Layers,
                moduleCount = snapshot.Modules.Count(module =>
                    string.Equals(module.Discipline, item.Id, StringComparison.OrdinalIgnoreCase))
            }).OrderBy(d => d.id);
            return Results.Ok(result);
        });

        group.MapPost("/disciplines", ([FromBody] UpsertDisciplineRequest request, [FromServices] ITopoGraphApplicationService topology, [FromServices] ProjectConfig config) =>
        {
            EnsureReady(topology, config);
            var id = request.Id?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(id))
                return Results.BadRequest(new { error = "discipline id is required." });
            if (request.Layers is null || request.Layers.Count == 0)
                return Results.BadRequest(new { error = "at least one layer is required." });

            var levels = request.Layers.Select(l => l.Level).ToList();
            if (levels.Distinct().Count() != levels.Count)
                return Results.BadRequest(new { error = "layer levels must be unique." });

            topology.UpsertDiscipline(id, request.DisplayName, request.RoleId ?? "coder", request.Layers);
            return Results.Ok(new { message = $"Discipline '{id}' saved.", id });
        });

        group.MapDelete("/disciplines/{disciplineId}", (string disciplineId, [FromServices] ITopoGraphApplicationService topology, [FromServices] ProjectConfig config) =>
        {
            EnsureReady(topology, config);
            var snapshot = topology.GetManagementSnapshot();
            var moduleCount = snapshot.Modules.Count(module =>
                string.Equals(module.Discipline, disciplineId, StringComparison.OrdinalIgnoreCase));
            if (moduleCount > 0)
                return Results.BadRequest(new { error = $"Discipline '{disciplineId}' still owns {moduleCount} modules." });

            var ok = topology.RemoveDiscipline(disciplineId);
            if (!ok) return Results.NotFound(new { error = $"Discipline not found: {disciplineId}" });
            return Results.Ok(new { message = $"Discipline removed: {disciplineId}" });
        });

        group.MapGet("/crossworks", ([FromServices] ITopoGraphApplicationService topology, [FromServices] ProjectConfig config) =>
        {
            EnsureReady(topology, config);
            return Results.Ok(topology.GetManagementSnapshot().CrossWorks);
        });

        group.MapPost("/crossworks", ([FromBody] TopologyCrossWorkDefinition request, [FromServices] ITopoGraphApplicationService topology, [FromServices] ProjectConfig config) =>
        {
            EnsureReady(topology, config);
            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.BadRequest(new { error = "crosswork.name is required." });

            request.Participants ??= [];
            request.Participants = request.Participants
                .Where(p => !string.IsNullOrWhiteSpace(p.ModuleName))
                .ToList();

            topology.SaveCrossWork(request);
            topology.BuildTopology();
            return Results.Ok(new { message = "CrossWork saved.", id = request.Id });
        });

        group.MapDelete("/crossworks/{id}", (string id, [FromServices] ITopoGraphApplicationService topology, [FromServices] ProjectConfig config) =>
        {
            EnsureReady(topology, config);
            var ok = topology.RemoveCrossWork(id);
            if (!ok) return Results.NotFound(new { error = $"CrossWork not found: {id}" });
            topology.BuildTopology();
            return Results.Ok(new { message = $"CrossWork removed: {id}" });
        });
    }

    private static double ScoreModule(KnowledgeNode module, string query)
    {
        var score = 0d;
        if (module.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) score += 3.0;
        if (module.RelativePath?.Contains(query, StringComparison.OrdinalIgnoreCase) == true) score += 2.0;
        if (!string.IsNullOrWhiteSpace(module.Summary) && module.Summary.Contains(query, StringComparison.OrdinalIgnoreCase)) score += 1.5;
        if (module.Keywords.Any(k => k.Contains(query, StringComparison.OrdinalIgnoreCase))) score += 1.2;
        if (module.Dependencies.Any(d => d.Contains(query, StringComparison.OrdinalIgnoreCase))) score += 0.5;
        return score;
    }

    private static (string discipline, int layer) ComputeCwOwnership(
        List<TopologyCrossWorkParticipantDefinition> participants,
        IEnumerable<TopologyModuleDefinition> modules)
    {
        if (participants is not { Count: > 0 })
            return ("root", 0);

        var participantNames = new HashSet<string>(participants.Select(p => p.ModuleName), StringComparer.OrdinalIgnoreCase);
        var disciplines = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var maxLayer = 0;

        foreach (var module in modules)
        {
            if (!participantNames.Contains(module.Name)) continue;
            disciplines.Add(module.Discipline);
            if (module.Layer > maxLayer) maxLayer = module.Layer;
        }

        return disciplines.Count == 1 ? (disciplines.First(), maxLayer) : ("root", 0);
    }

    private static void EnsureReady(ITopoGraphApplicationService topology, ProjectConfig config)
    {
        if (!config.HasProject)
            throw new InvalidOperationException("Project is not configured.");

        topology.BuildTopology();
    }
}

public sealed class BeginTaskRequest
{
    public List<string>? ModuleNames { get; set; }
}

public sealed class CondenseNodeRequest
{
    public string NodeIdOrName { get; set; } = string.Empty;
    public int? MaxSourceMemories { get; set; }
}

public sealed class EvolveKnowledgeRequest
{
    public string? NodeIdOrName { get; set; }
    public int? MaxSuggestions { get; set; }
}

public sealed class CondenseAllRequest
{
    public int? MaxSourceMemories { get; set; }
}

public sealed class UpsertModuleRequest
{
    public string Discipline { get; set; } = string.Empty;
    public TopologyModuleDefinition? Module { get; set; }
}

public sealed class UpsertDisciplineRequest
{
    public string Id { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? RoleId { get; set; }
    public List<LayerDefinition> Layers { get; set; } = [];
}
