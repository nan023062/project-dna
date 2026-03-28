using Dna.Memory.Models;
using Dna.Memory.Store;
using Microsoft.Extensions.Logging;

namespace Dna.Knowledge;

/// <summary>
/// 记忆引擎 — 记忆读写、召回、查询、维护。全部委托 MemoryStore。
/// </summary>
public sealed class MemoryEngine : IMemoryEngine
{
    private readonly MemoryStore _store;
    private readonly ILogger<MemoryEngine> _logger;

    internal MemoryEngine(DnaServiceHolder holder, ILogger<MemoryEngine> logger)
    {
        _store = holder.Store;
        _logger = logger;
    }

    public Task<MemoryEntry> RememberAsync(RememberRequest request)
        => _store.RememberAsync(request);

    public Task<RecallResult> RecallAsync(RecallQuery query)
        => _store.RecallAsync(query);

    public MemoryEntry? GetMemoryById(string id) => _store.GetById(id);

    public List<MemoryEntry> QueryMemories(MemoryFilter filter) => _store.Query(filter);

    public void VerifyMemory(string memoryId) => _store.VerifyMemory(memoryId);

    public Task<MemoryEntry> UpdateMemoryAsync(string memoryId, RememberRequest request)
        => _store.UpdateMemoryAsync(memoryId, request);

    public bool DeleteMemory(string id) => _store.Delete(id);

    public Task<List<MemoryEntry>> RememberBatchAsync(List<RememberRequest> requests)
        => _store.RememberBatchAsync(requests);

    public List<MemoryEntry> GetConstraintChain(string memoryId)
        => _store.GetConstraintChain(memoryId);

    public MemoryStats GetMemoryStats() => _store.GetStats();

    public FeatureKnowledgeSummary GetFeatureSummary(string featureId)
        => _store.GetFeatureSummary(featureId);

    public DisciplineKnowledgeSummary GetDisciplineSummary(string disciplineId)
        => _store.GetDisciplineSummary(disciplineId);

    public int MemoryCount() => _store.Count();

    public (int imported, int skipped) RebuildIndex(bool rewriteJson = false) => _store.RebuildIndex(rewriteJson);
    public (int added, int removed, int skipped) SyncFromJson() => _store.SyncFromJson();
    public (int exported, int skipped) ExportToJson() => _store.ExportToJson();
    public int DecayStaleMemories() => _store.DecayStaleMemories();

    public void Initialize(string storePath) => _store.Initialize(storePath);
}
