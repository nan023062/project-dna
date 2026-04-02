using Dna.Knowledge;
using Dna.Memory.Models;
using Dna.Memory.Store;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Dna.Memory.Services;

/// <summary>
/// 记忆写入服务 — remember 的完整实现。
/// 写入时自动完成：生成 ULID → 生成 embedding → 写入 JSON + SQLite + FTS → 更新向量索引。
/// </summary>
internal class MemoryWriter
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private readonly MemoryStore _store;
    private readonly ITopoGraphStore _topoGraphStore;
    private readonly EmbeddingService _embeddingService;
    private readonly VectorIndex _vectorIndex;
    private readonly ILogger<MemoryWriter> _logger;

    public MemoryWriter(
        MemoryStore store,
        ITopoGraphStore topoGraphStore,
        EmbeddingService embeddingService,
        VectorIndex vectorIndex,
        ILogger<MemoryWriter> logger)
    {
        _store = store;
        _topoGraphStore = topoGraphStore;
        _embeddingService = embeddingService;
        _vectorIndex = vectorIndex;
        _logger = logger;
    }

    /// <summary>写入一条记忆（完整流程）</summary>
    public async Task<MemoryEntry> RememberAsync(RememberRequest request)
    {
        ValidateSystemTagPayload(request.Tags, request.Content);
        var nodeId = _topoGraphStore.ResolveNodeIdCandidates(request.NodeId, strict: true).FirstOrDefault();

        var entry = new MemoryEntry
        {
            Id = UlidGenerator.New(),
            Type = request.Type,
            NodeType = request.ResolvedNodeType,
            Source = request.Source,
            Content = request.Content,
            Summary = request.Summary ?? GenerateAutoSummary(request.Content),
            Disciplines = request.Disciplines,
            Features = request.Features ?? [],
            NodeId = nodeId,
            PathPatterns = request.PathPatterns ?? [],
            Tags = request.Tags,
            ParentId = request.ParentId,
            Stage = request.Stage ?? MemoryStage.LongTerm,
            Importance = request.Importance,
            ExternalSourceUrl = request.ExternalSourceUrl,
            ExternalSourceId = request.ExternalSourceId,
            Freshness = FreshnessStatus.Fresh,
            CreatedAt = DateTime.UtcNow
        };

        var embeddingText = $"{entry.Summary ?? ""} {entry.Content}";
        entry.Embedding = await _embeddingService.GenerateEmbeddingAsync(embeddingText);

        _store.Insert(entry);

        if (entry.Embedding != null)
            _vectorIndex.Upsert(entry.Id, entry.Embedding);

        _logger.LogInformation("remember: {Type}/{NodeType} [{Id}] {Summary}",
            entry.Type, entry.NodeType, entry.Id, Truncate(entry.Summary, 60));

        return entry;
    }

    /// <summary>更新一条记忆</summary>
    public async Task<MemoryEntry> UpdateAsync(string memoryId, RememberRequest request)
    {
        ValidateSystemTagPayload(request.Tags, request.Content);
        var nodeId = _topoGraphStore.ResolveNodeIdCandidates(request.NodeId, strict: true).FirstOrDefault();

        var existing = _store.GetById(memoryId);
        if (existing == null)
            throw new InvalidOperationException($"记忆不存在: {memoryId}");

        var updated = new MemoryEntry
        {
            Id = memoryId,
            Type = request.Type,
            NodeType = request.ResolvedNodeType,
            Source = request.Source,
            Content = request.Content,
            Summary = request.Summary ?? GenerateAutoSummary(request.Content),
            Disciplines = request.Disciplines,
            Features = request.Features ?? [],
            NodeId = nodeId,
            PathPatterns = request.PathPatterns ?? [],
            Tags = request.Tags,
            ParentId = request.ParentId,
            Stage = request.Stage ?? existing.Stage,
            Importance = request.Importance,
            ExternalSourceUrl = request.ExternalSourceUrl,
            ExternalSourceId = request.ExternalSourceId,
            Freshness = FreshnessStatus.Fresh,
            CreatedAt = existing.CreatedAt,
            LastVerifiedAt = DateTime.UtcNow,
            Version = existing.Version + 1,
            EvolutionChain = [.. existing.EvolutionChain, existing.Id]
        };

        var embeddingText = $"{updated.Summary ?? ""} {updated.Content}";
        updated.Embedding = await _embeddingService.GenerateEmbeddingAsync(embeddingText);

        _store.Update(updated);

        if (updated.Embedding != null)
            _vectorIndex.Upsert(updated.Id, updated.Embedding);

        _logger.LogDebug("update memory: [{Id}] v{Version}", memoryId, updated.Version);
        return updated;
    }

    /// <summary>标记一条记忆被新知识取代</summary>
    public void Supersede(string oldMemoryId, string newMemoryId)
    {
        _store.UpdateFreshness(oldMemoryId, FreshnessStatus.Superseded);
        _vectorIndex.Remove(oldMemoryId);
        _logger.LogDebug("supersede: {Old} → {New}", oldMemoryId, newMemoryId);
    }

    /// <summary>批量写入（用于迁移等场景）</summary>
    public async Task<List<MemoryEntry>> RememberBatchAsync(List<RememberRequest> requests)
    {
        var entries = new List<MemoryEntry>();
        foreach (var request in requests)
        {
            entries.Add(await RememberAsync(request));
        }
        return entries;
    }

    /// <summary>验证一条记忆仍有效</summary>
    public void Verify(string memoryId)
    {
        _store.Verify(memoryId);
        _logger.LogDebug("verify: [{Id}]", memoryId);
    }

    private static string? GenerateAutoSummary(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;

        var firstLine = content.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim().TrimStart('#').Trim())
            .FirstOrDefault(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("<!--"));

        return firstLine != null ? Truncate(firstLine, 120) : null;
    }

    private static string? Truncate(string? text, int maxLen)
        => text == null ? null : (text.Length <= maxLen ? text : text[..maxLen] + "…");

    private static void ValidateSystemTagPayload(List<string> tags, string content)
    {
        if (tags.Contains(WellKnownTags.Identity, StringComparer.OrdinalIgnoreCase))
        {
            var payload = DeserializeRequired<IdentityPayload>(content, WellKnownTags.Identity);
            if (string.IsNullOrWhiteSpace(payload.Summary))
                throw new InvalidOperationException("#identity payload 必须包含非空 summary 字段。");
        }

        if (tags.Contains(WellKnownTags.Lesson, StringComparer.OrdinalIgnoreCase))
        {
            var payload = DeserializeRequired<LessonPayload>(content, WellKnownTags.Lesson);
            if (string.IsNullOrWhiteSpace(payload.Title) || string.IsNullOrWhiteSpace(payload.Context))
                throw new InvalidOperationException("#lesson payload 必须包含非空 title/context 字段。");
        }

        if (tags.Contains(WellKnownTags.ActiveTask, StringComparer.OrdinalIgnoreCase))
        {
            var payload = DeserializeRequired<ActiveTaskPayload>(content, WellKnownTags.ActiveTask);
            if (string.IsNullOrWhiteSpace(payload.Task))
                throw new InvalidOperationException("#active-task payload 必须包含非空 task 字段。");
        }
    }

    private static T DeserializeRequired<T>(string content, string tag)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<T>(content, JsonOpts);
            return payload ?? throw new InvalidOperationException($"标签 {tag} 的 content 不能为空 JSON。");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"标签 {tag} 的 content 必须是合法 JSON。", ex);
        }
    }
}
