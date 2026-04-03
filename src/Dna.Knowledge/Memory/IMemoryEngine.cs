using Dna.Memory.Models;

namespace Dna.Knowledge;

public interface IMemoryEngine
{
    Task<MemoryEntry> RememberAsync(RememberRequest request);
    Task<RecallResult> RecallAsync(RecallQuery query);
    MemoryEntry? GetMemoryById(string id);
    List<MemoryEntry> QueryMemories(MemoryFilter filter);
    void VerifyMemory(string memoryId);
    Task<MemoryEntry> UpdateMemoryAsync(string memoryId, RememberRequest request);
    bool DeleteMemory(string id);
    Task<List<MemoryEntry>> RememberBatchAsync(List<RememberRequest> requests);
    List<MemoryEntry> GetConstraintChain(string memoryId);
    MemoryStats GetMemoryStats();
    FeatureKnowledgeSummary GetFeatureSummary(string featureId);
    DisciplineKnowledgeSummary GetDisciplineSummary(string disciplineId);
    int MemoryCount();
    int DecayStaleMemories();

    void Initialize(string storePath);
}
