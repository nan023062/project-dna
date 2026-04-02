using Dna.Memory.Models;
using Dna.Memory.Store;
using Microsoft.Extensions.Logging;

namespace Dna.Knowledge;

/// <summary>
/// 记忆引擎 — 记忆门面，统一组织写入、读取、召回与维护能力。
/// </summary>
public sealed class MemoryEngine : IMemoryEngine
{
    private readonly MemoryStore _store;
    private readonly ILogger<MemoryEngine> _logger;

    public MemoryEngine(MemoryStore store, ILogger<MemoryEngine> logger)
    {
        _store = store;
        _logger = logger;
    }

    public Task<MemoryEntry> RememberAsync(RememberRequest request)
        => _store.WriterService.RememberAsync(request);

    public Task<RecallResult> RecallAsync(RecallQuery query)
        => _store.RecallService.RecallAsync(query);

    public MemoryEntry? GetMemoryById(string id) => _store.ReaderService.GetById(id);

    public List<MemoryEntry> QueryMemories(MemoryFilter filter) => _store.ReaderService.Query(filter);

    public void VerifyMemory(string memoryId) => _store.WriterService.Verify(memoryId);

    public Task<MemoryEntry> UpdateMemoryAsync(string memoryId, RememberRequest request)
        => _store.WriterService.UpdateAsync(memoryId, request);

    public bool DeleteMemory(string id) => _store.Delete(id);

    public Task<List<MemoryEntry>> RememberBatchAsync(List<RememberRequest> requests)
        => _store.WriterService.RememberBatchAsync(requests);

    public List<MemoryEntry> GetConstraintChain(string memoryId)
        => _store.ReaderService.GetConstraintChain(memoryId);

    public MemoryStats GetMemoryStats() => _store.ReaderService.GetStats();

    public FeatureKnowledgeSummary GetFeatureSummary(string featureId)
        => _store.ReaderService.GetFeatureSummary(featureId);

    public DisciplineKnowledgeSummary GetDisciplineSummary(string disciplineId)
        => _store.ReaderService.GetDisciplineSummary(disciplineId);

    public int MemoryCount() => _store.Count();

    public (int imported, int skipped) RebuildIndex(bool rewriteJson = false) => _store.RebuildIndex(rewriteJson);
    public (int added, int removed, int skipped) SyncFromJson() => _store.SyncFromJson();
    public (int exported, int skipped) ExportToJson() => _store.ExportToJson();
    public int DecayStaleMemories() => _store.DecayStaleMemories();

    public void Initialize(string storePath) => _store.Initialize(storePath);
}
