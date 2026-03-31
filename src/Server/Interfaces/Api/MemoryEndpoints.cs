using Dna.Auth;
using Dna.Knowledge;
using Dna.Knowledge.Models;
using Dna.Memory.Models;
using Dna.Core.Config;
using System.Security.Claims;

namespace Dna.Interfaces.Api;

public static class MemoryEndpoints
{
    public static void MapMemoryEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/memory");
        group.RequireAuthorization(ServerPolicies.ViewerOrAbove);

        group.MapPost("/remember", async (RememberRequest request, IMemoryEngine memory, ProjectConfig config, ClaimsPrincipal principal) =>
        {
            EnsureReady(memory, config);
            var guard = RequireAdminForLegacyFormalWrite(principal);
            if (guard is not null) return guard;
            var entry = await memory.RememberAsync(request);
            return Results.Ok(entry);
        }).RequireAuthorization(ServerPolicies.AdminOnly);

        group.MapPost("/batch", async (List<RememberRequest> requests, IMemoryEngine memory, ProjectConfig config, ClaimsPrincipal principal) =>
        {
            EnsureReady(memory, config);
            var guard = RequireAdminForLegacyFormalWrite(principal);
            if (guard is not null) return guard;
            if (requests.Count > 50)
                return Results.BadRequest(new { error = $"单次最多 50 条，当前 {requests.Count} 条" });
            var entries = await memory.RememberBatchAsync(requests);
            return Results.Ok(new { success = entries.Count, ids = entries.Select(e => e.Id).ToList() });
        }).RequireAuthorization(ServerPolicies.AdminOnly);

        group.MapPost("/recall", async (RecallQuery query, IMemoryEngine memory, ProjectConfig config) =>
        {
            EnsureReady(memory, config);
            var result = await memory.RecallAsync(query);
            return Results.Ok(result);
        }).RequireAuthorization(ServerPolicies.ViewerOrAbove);

        group.MapGet("/query", (
            string? nodeTypes, string? layers, string? disciplines, string? features,
            string? types, string? tags, string? nodeId, string? freshness,
            int? limit, int? offset,
            IMemoryEngine memory,
            ProjectConfig config) =>
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
        }).RequireAuthorization(ServerPolicies.ViewerOrAbove);

        group.MapGet("/{id}", (string id, IMemoryEngine memory, ProjectConfig config) =>
        {
            EnsureReady(memory, config);
            var entry = memory.GetMemoryById(id);
            return entry != null ? Results.Ok(entry) : Results.NotFound();
        }).RequireAuthorization(ServerPolicies.ViewerOrAbove);

        group.MapGet("/{id}/chain", (string id, IMemoryEngine memory, ProjectConfig config) =>
        {
            EnsureReady(memory, config);
            return Results.Ok(memory.GetConstraintChain(id));
        }).RequireAuthorization(ServerPolicies.ViewerOrAbove);

        group.MapPut("/{id}", async (string id, RememberRequest request, IMemoryEngine memory, ProjectConfig config, ClaimsPrincipal principal) =>
        {
            try
            {
                EnsureReady(memory, config);
                var guard = RequireAdminForLegacyFormalWrite(principal);
                if (guard is not null) return guard;
                var updated = await memory.UpdateMemoryAsync(id, request);
                return Results.Ok(updated);
            }
            catch (InvalidOperationException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        }).RequireAuthorization(ServerPolicies.AdminOnly);

        group.MapPost("/{id}/verify", (string id, IMemoryEngine memory, ProjectConfig config) =>
        {
            EnsureReady(memory, config);
            var entry = memory.GetMemoryById(id);
            if (entry == null) return Results.NotFound();
            memory.VerifyMemory(id);
            return Results.Ok(new { message = $"Memory [{id}] verified" });
        }).RequireAuthorization(ServerPolicies.EditorOrAbove);

        group.MapDelete("/{id}", (string id, IMemoryEngine memory, ProjectConfig config, ClaimsPrincipal principal) =>
        {
            EnsureReady(memory, config);
            var guard = RequireAdminForLegacyFormalWrite(principal);
            if (guard is not null) return guard;
            var deleted = memory.DeleteMemory(id);
            return deleted ? Results.Ok(new { message = $"Deleted [{id}]" }) : Results.NotFound();
        }).RequireAuthorization(ServerPolicies.AdminOnly);

        group.MapGet("/feature/{featureId}", (string featureId, IMemoryEngine memory, ProjectConfig config) =>
        {
            EnsureReady(memory, config);
            return Results.Ok(memory.GetFeatureSummary(featureId));
        }).RequireAuthorization(ServerPolicies.ViewerOrAbove);

        group.MapGet("/discipline/{disciplineId}", (string disciplineId, IMemoryEngine memory, ProjectConfig config) =>
        {
            EnsureReady(memory, config);
            return Results.Ok(memory.GetDisciplineSummary(disciplineId));
        }).RequireAuthorization(ServerPolicies.ViewerOrAbove);

        group.MapGet("/stats", (IMemoryEngine memory, ProjectConfig config) =>
        {
            EnsureReady(memory, config);
            return Results.Ok(memory.GetMemoryStats());
        }).RequireAuthorization(ServerPolicies.ViewerOrAbove);

        group.MapPost("/governance/check-freshness", (IGovernanceEngine governance, IMemoryEngine memory, ProjectConfig config) =>
        {
            EnsureReady(memory, config);
            var decayed = governance.CheckFreshness();
            return Results.Ok(new { message = $"完成鲜活度检查，降级了 {decayed} 条记忆", decayedCount = decayed });
        }).RequireAuthorization(ServerPolicies.AdminOnly);

        group.MapPost("/governance/detect-conflicts", (IGovernanceEngine governance, IMemoryEngine memory, ProjectConfig config) =>
        {
            EnsureReady(memory, config);
            var conflicts = governance.DetectMemoryConflicts();
            return Results.Ok(new { message = $"完成冲突检测，标记了 {conflicts} 处冲突", conflictCount = conflicts });
        }).RequireAuthorization(ServerPolicies.AdminOnly);

        group.MapPost("/governance/archive-stale", (IGovernanceEngine governance, IMemoryEngine memory, ProjectConfig config) =>
        {
            EnsureReady(memory, config);
            var archived = governance.ArchiveStaleMemories(TimeSpan.FromDays(30));
            return Results.Ok(new { message = $"完成归档操作，归档了 {archived} 条陈旧记忆", archivedCount = archived });
        }).RequireAuthorization(ServerPolicies.AdminOnly);

        group.MapPost("/index/rebuild", (bool? rewriteJson, IMemoryEngine memory, ProjectConfig config) =>
        {
            EnsureReady(memory, config);
            var (imported, skipped) = memory.RebuildIndex(rewriteJson ?? false);
            return Results.Ok(new
            {
                message = $"搜索索引重建完成：更新 {imported} 条，异常 {skipped} 条",
                indexed = imported,
                skipped,
                rewriteJson = rewriteJson ?? false
            });
        }).RequireAuthorization(ServerPolicies.AdminOnly);

        group.MapPost("/index/sync", (IMemoryEngine memory, ProjectConfig config) =>
        {
            EnsureReady(memory, config);
            var (added, removed, skipped) = memory.SyncFromJson();
            return Results.Ok(new
            {
                message = $"兼容接口执行完成：已重建搜索索引 {added} 条",
                indexed = added,
                removed,
                skipped
            });
        }).RequireAuthorization(ServerPolicies.AdminOnly);

        group.MapPost("/index/export", (IMemoryEngine memory, ProjectConfig config) =>
        {
            EnsureReady(memory, config);
            var (exported, skipped) = memory.ExportToJson();
            return Results.Ok(new
            {
                message = "当前为纯 DB 存储，不再导出记忆 JSON。",
                exported,
                skipped
            });
        }).RequireAuthorization(ServerPolicies.AdminOnly);
    }

    private static List<string>? SplitOrNull(string? csv) =>
        string.IsNullOrWhiteSpace(csv) ? null :
            csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static List<T>? ParseEnumList<T>(string? csv) where T : struct, Enum =>
        string.IsNullOrWhiteSpace(csv) ? null :
            csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => Enum.TryParse<T>(s, true, out var v) ? v : (T?)null)
                .Where(v => v.HasValue).Select(v => v!.Value).ToList();

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
    }

    private static IResult? RequireAdminForLegacyFormalWrite(ClaimsPrincipal _) => null;
}
