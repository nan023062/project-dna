using Dna.Knowledge;
using Dna.Knowledge.Models;
using Dna.Memory.Models;
using Dna.Core.Config;

namespace Dna.Interfaces.Api;

public static class MemoryEndpoints
{
    public static void MapMemoryEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/memory");

        group.MapPost("/remember", async (RememberRequest request, IMemoryEngine memory, ProjectConfig config) =>
        {
            EnsureReady(memory, config);
            var entry = await memory.RememberAsync(request);
            return Results.Ok(entry);
        });

        group.MapPost("/batch", async (List<RememberRequest> requests, IMemoryEngine memory, ProjectConfig config) =>
        {
            EnsureReady(memory, config);
            if (requests.Count > 50)
                return Results.BadRequest(new { error = $"单次最多 50 条，当前 {requests.Count} 条" });
            var entries = await memory.RememberBatchAsync(requests);
            return Results.Ok(new { success = entries.Count, ids = entries.Select(e => e.Id).ToList() });
        });

        group.MapPost("/recall", async (RecallQuery query, IMemoryEngine memory, ProjectConfig config) =>
        {
            EnsureReady(memory, config);
            var result = await memory.RecallAsync(query);
            return Results.Ok(result);
        });

        group.MapGet("/query", (
            string? layers, string? disciplines, string? features,
            string? types, string? tags, string? nodeId, string? freshness,
            int limit, int offset,
            IMemoryEngine memory,
            ProjectConfig config) =>
        {
            EnsureReady(memory, config);
            var filter = new MemoryFilter
            {
                Layers = ParseEnumList<KnowledgeLayer>(layers),
                Disciplines = SplitOrNull(disciplines),
                Features = SplitOrNull(features),
                Types = ParseEnumList<MemoryType>(types),
                Tags = SplitOrNull(tags),
                NodeId = nodeId,
                Freshness = ParseEnum<FreshnessFilter>(freshness) ?? FreshnessFilter.FreshAndAging,
                Limit = Math.Clamp(limit > 0 ? limit : 50, 1, 200),
                Offset = Math.Max(offset, 0)
            };
            return Results.Ok(memory.QueryMemories(filter));
        });

        group.MapGet("/{id}", (string id, IMemoryEngine memory, ProjectConfig config) =>
        {
            EnsureReady(memory, config);
            var entry = memory.GetMemoryById(id);
            return entry != null ? Results.Ok(entry) : Results.NotFound();
        });

        group.MapGet("/{id}/chain", (string id, IMemoryEngine memory, ProjectConfig config) =>
        {
            EnsureReady(memory, config);
            return Results.Ok(memory.GetConstraintChain(id));
        });

        group.MapPut("/{id}", async (string id, RememberRequest request, IMemoryEngine memory, ProjectConfig config) =>
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

        group.MapPost("/{id}/verify", (string id, IMemoryEngine memory, ProjectConfig config) =>
        {
            EnsureReady(memory, config);
            var entry = memory.GetMemoryById(id);
            if (entry == null) return Results.NotFound();
            memory.VerifyMemory(id);
            return Results.Ok(new { message = $"Memory [{id}] verified" });
        });

        group.MapDelete("/{id}", (string id, IMemoryEngine memory, ProjectConfig config) =>
        {
            EnsureReady(memory, config);
            var deleted = memory.DeleteMemory(id);
            return deleted ? Results.Ok(new { message = $"Deleted [{id}]" }) : Results.NotFound();
        });

        group.MapGet("/feature/{featureId}", (string featureId, IMemoryEngine memory, ProjectConfig config) =>
        {
            EnsureReady(memory, config);
            return Results.Ok(memory.GetFeatureSummary(featureId));
        });

        group.MapGet("/discipline/{disciplineId}", (string disciplineId, IMemoryEngine memory, ProjectConfig config) =>
        {
            EnsureReady(memory, config);
            return Results.Ok(memory.GetDisciplineSummary(disciplineId));
        });

        group.MapGet("/stats", (IMemoryEngine memory, ProjectConfig config) =>
        {
            EnsureReady(memory, config);
            return Results.Ok(memory.GetMemoryStats());
        });

        group.MapPost("/governance/check-freshness", (IGovernanceEngine governance, IMemoryEngine memory, ProjectConfig config) =>
        {
            EnsureReady(memory, config);
            var decayed = governance.CheckFreshness();
            return Results.Ok(new { message = $"完成鲜活度检查，降级了 {decayed} 条记忆", decayedCount = decayed });
        });

        group.MapPost("/governance/detect-conflicts", (IGovernanceEngine governance, IMemoryEngine memory, ProjectConfig config) =>
        {
            EnsureReady(memory, config);
            var conflicts = governance.DetectMemoryConflicts();
            return Results.Ok(new { message = $"完成冲突检测，标记了 {conflicts} 处冲突", conflictCount = conflicts });
        });

        group.MapPost("/governance/archive-stale", (IGovernanceEngine governance, IMemoryEngine memory, ProjectConfig config) =>
        {
            EnsureReady(memory, config);
            var archived = governance.ArchiveStaleMemories(TimeSpan.FromDays(30));
            return Results.Ok(new { message = $"完成归档操作，归档了 {archived} 条陈旧记忆", archivedCount = archived });
        });

        group.MapPost("/index/rebuild", (bool? rewriteJson, IMemoryEngine memory, ProjectConfig config) =>
        {
            EnsureReady(memory, config);
            var (imported, skipped) = memory.RebuildIndex(rewriteJson ?? false);
            return Results.Ok(new { message = $"全量重建完成：导入 {imported} 条，跳过 {skipped} 条", imported, skipped, rewriteJson = rewriteJson ?? false });
        });

        group.MapPost("/index/sync", (IMemoryEngine memory, ProjectConfig config) =>
        {
            EnsureReady(memory, config);
            var (added, removed, skipped) = memory.SyncFromJson();
            return Results.Ok(new { message = $"增量同步完成：新增 {added} 条，移除孤儿 {removed} 条，跳过 {skipped} 条", added, removed, skipped });
        });

        group.MapPost("/index/export", (IMemoryEngine memory, ProjectConfig config) =>
        {
            EnsureReady(memory, config);
            var (exported, skipped) = memory.ExportToJson();
            return Results.Ok(new { message = $"导出完成：{exported} 条记忆已写入 JSON，跳过 {skipped} 条", exported, skipped });
        });
    }

    private static List<string>? SplitOrNull(string? csv) =>
        string.IsNullOrWhiteSpace(csv) ? null :
            csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static List<T>? ParseEnumList<T>(string? csv) where T : struct, Enum =>
        string.IsNullOrWhiteSpace(csv) ? null :
            csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => Enum.TryParse<T>(s, true, out var v) ? v : (T?)null)
                .Where(v => v.HasValue).Select(v => v!.Value).ToList();

    private static T? ParseEnum<T>(string? value) where T : struct, Enum =>
        Enum.TryParse<T>(value, true, out var result) ? result : null;

    private static void EnsureReady(IMemoryEngine memory, ProjectConfig config)
    {
    }
}
