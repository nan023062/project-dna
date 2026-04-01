using Dna.Core.Config;
using Dna.Knowledge;
using Dna.Memory.Models;
using Microsoft.AspNetCore.Mvc;

namespace Dna.Client.Interfaces.Api;

public static class ClientLocalMemoryApiEndpoints
{
    public static void MapClientLocalMemoryApiEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/memory");

        group.MapPost("/remember", async ([FromBody] RememberRequest request, [FromServices] IMemoryEngine memory, [FromServices] ProjectConfig config) =>
        {
            EnsureReady(memory, config);
            var entry = await memory.RememberAsync(request);
            return Results.Ok(entry);
        });

        group.MapPost("/batch", async ([FromBody] List<RememberRequest> requests, [FromServices] IMemoryEngine memory, [FromServices] ProjectConfig config) =>
        {
            EnsureReady(memory, config);
            if (requests.Count > 50)
                return Results.BadRequest(new { error = $"A batch supports at most 50 entries, current count is {requests.Count}." });

            var entries = await memory.RememberBatchAsync(requests);
            return Results.Ok(new { success = entries.Count, ids = entries.Select(e => e.Id).ToList() });
        });

        group.MapPost("/recall", async ([FromBody] RecallQuery query, [FromServices] IMemoryEngine memory, [FromServices] ProjectConfig config) =>
        {
            EnsureReady(memory, config);
            var result = await memory.RecallAsync(query);
            return Results.Ok(result);
        });

        group.MapGet("/query", (
            string? nodeTypes, string? layers, string? disciplines, string? features,
            string? types, string? tags, string? nodeId, string? freshness,
            int? limit, int? offset,
            [FromServices] IMemoryEngine memory,
            [FromServices] ProjectConfig config) =>
        {
            EnsureReady(memory, config);
            var filter = new MemoryFilter
            {
                NodeTypes = ParseNodeTypeList(nodeTypes, layers),
                Disciplines = SplitOrNull(disciplines),
                Features = SplitOrNull(features),
                Types = ParseEnumList<MemoryType>(types),
                Tags = SplitOrNull(tags),
                NodeId = nodeId,
                Freshness = ParseEnum<FreshnessFilter>(freshness) ?? FreshnessFilter.FreshAndAging,
                Limit = Math.Clamp(limit is > 0 ? limit.Value : 50, 1, 200),
                Offset = Math.Max(offset ?? 0, 0)
            };
            return Results.Ok(memory.QueryMemories(filter));
        });

        group.MapGet("/{id}", (string id, [FromServices] IMemoryEngine memory, [FromServices] ProjectConfig config) =>
        {
            EnsureReady(memory, config);
            var entry = memory.GetMemoryById(id);
            return entry != null ? Results.Ok(entry) : Results.NotFound();
        });

        group.MapGet("/{id}/chain", (string id, [FromServices] IMemoryEngine memory, [FromServices] ProjectConfig config) =>
        {
            EnsureReady(memory, config);
            return Results.Ok(memory.GetConstraintChain(id));
        });

        group.MapPut("/{id}", async (string id, [FromBody] RememberRequest request, [FromServices] IMemoryEngine memory, [FromServices] ProjectConfig config) =>
        {
            try
            {
                EnsureReady(memory, config);
                var updated = await memory.UpdateMemoryAsync(id, request);
                return Results.Ok(updated);
            }
            catch (InvalidOperationException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        });

        group.MapPost("/{id}/verify", (string id, [FromServices] IMemoryEngine memory, [FromServices] ProjectConfig config) =>
        {
            EnsureReady(memory, config);
            var entry = memory.GetMemoryById(id);
            if (entry == null) return Results.NotFound();
            memory.VerifyMemory(id);
            return Results.Ok(new { message = $"Memory [{id}] verified" });
        });

        group.MapDelete("/{id}", (string id, [FromServices] IMemoryEngine memory, [FromServices] ProjectConfig config) =>
        {
            EnsureReady(memory, config);
            var deleted = memory.DeleteMemory(id);
            return deleted ? Results.Ok(new { message = $"Deleted [{id}]" }) : Results.NotFound();
        });

        group.MapGet("/feature/{featureId}", (string featureId, [FromServices] IMemoryEngine memory, [FromServices] ProjectConfig config) =>
        {
            EnsureReady(memory, config);
            return Results.Ok(memory.GetFeatureSummary(featureId));
        });

        group.MapGet("/discipline/{disciplineId}", (string disciplineId, [FromServices] IMemoryEngine memory, [FromServices] ProjectConfig config) =>
        {
            EnsureReady(memory, config);
            return Results.Ok(memory.GetDisciplineSummary(disciplineId));
        });

        group.MapGet("/stats", ([FromServices] IMemoryEngine memory, [FromServices] ProjectConfig config) =>
        {
            EnsureReady(memory, config);
            return Results.Ok(memory.GetMemoryStats());
        });

        group.MapPost("/governance/check-freshness", ([FromServices] IGovernanceEngine governance, [FromServices] IMemoryEngine memory, [FromServices] ProjectConfig config) =>
        {
            EnsureReady(memory, config);
            var decayed = governance.CheckFreshness();
            return Results.Ok(new { message = $"Decayed {decayed} memories.", decayedCount = decayed });
        });

        group.MapPost("/governance/detect-conflicts", ([FromServices] IGovernanceEngine governance, [FromServices] IMemoryEngine memory, [FromServices] ProjectConfig config) =>
        {
            EnsureReady(memory, config);
            var conflicts = governance.DetectMemoryConflicts();
            return Results.Ok(new { message = $"Marked {conflicts} conflicts.", conflictCount = conflicts });
        });

        group.MapPost("/governance/archive-stale", ([FromServices] IGovernanceEngine governance, [FromServices] IMemoryEngine memory, [FromServices] ProjectConfig config) =>
        {
            EnsureReady(memory, config);
            var archived = governance.ArchiveStaleMemories(TimeSpan.FromDays(30));
            return Results.Ok(new { message = $"Archived {archived} stale memories.", archivedCount = archived });
        });

        group.MapPost("/index/rebuild", (bool? rewriteJson, [FromServices] IMemoryEngine memory, [FromServices] ProjectConfig config) =>
        {
            EnsureReady(memory, config);
            var (imported, skipped) = memory.RebuildIndex(rewriteJson ?? false);
            return Results.Ok(new
            {
                message = $"Rebuilt search index: indexed {imported}, skipped {skipped}.",
                indexed = imported,
                skipped,
                rewriteJson = rewriteJson ?? false
            });
        });

        group.MapPost("/index/sync", ([FromServices] IMemoryEngine memory, [FromServices] ProjectConfig config) =>
        {
            EnsureReady(memory, config);
            var (added, removed, skipped) = memory.SyncFromJson();
            return Results.Ok(new
            {
                message = $"Synced search index: added {added}, removed {removed}, skipped {skipped}.",
                indexed = added,
                removed,
                skipped
            });
        });

        group.MapPost("/index/export", ([FromServices] IMemoryEngine memory, [FromServices] ProjectConfig config) =>
        {
            EnsureReady(memory, config);
            var (exported, skipped) = memory.ExportToJson();
            return Results.Ok(new
            {
                message = "Current storage is database-first; JSON export is kept for compatibility.",
                exported,
                skipped
            });
        });
    }

    private static List<string>? SplitOrNull(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? null
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static List<T>? ParseEnumList<T>(string? csv) where T : struct, Enum =>
        string.IsNullOrWhiteSpace(csv)
            ? null
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => Enum.TryParse<T>(s, true, out var value) ? value : (T?)null)
                .Where(v => v.HasValue)
                .Select(v => v!.Value)
                .ToList();

    private static List<NodeType>? ParseNodeTypeList(string? nodeTypes, string? legacyLayers = null)
    {
        var merged = new List<string>();
        if (!string.IsNullOrWhiteSpace(nodeTypes))
            merged.AddRange(nodeTypes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (!string.IsNullOrWhiteSpace(legacyLayers))
            merged.AddRange(legacyLayers.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (merged.Count == 0) return null;

        var parsed = merged
            .Select(item => NodeTypeCompat.TryParse(item, out var nodeType) ? (NodeType?)nodeType : null)
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .Distinct()
            .ToList();

        return parsed.Count > 0 ? parsed : null;
    }

    private static T? ParseEnum<T>(string? value) where T : struct, Enum =>
        Enum.TryParse<T>(value, true, out var result) ? result : null;

    private static void EnsureReady(IMemoryEngine memory, ProjectConfig config)
    {
        if (!config.HasProject)
            throw new InvalidOperationException("Project is not configured.");
    }
}
