namespace Dna.Knowledge;

public interface IGovernanceEngine
{
    GovernanceReport ValidateArchitecture();
    int CheckFreshness();
    int DetectMemoryConflicts();
    int ArchiveStaleMemories(TimeSpan staleThreshold);
    Task<IReadOnlyList<GovernanceActiveModule>> GetActiveModulesAsync(TimeSpan activeWindow, int maxModules = 50);
    Task<GovernanceScanResult> ScanAsync(GovernanceScanRequest request);
    Task<KnowledgeEvolutionReport> EvolveKnowledgeAsync(string? nodeIdOrName = null, int maxSuggestions = 50);
    Task<KnowledgeCondenseResult> CondenseNodeKnowledgeAsync(string nodeIdOrName, int maxSourceMemories = 200);
    Task<List<KnowledgeCondenseResult>> CondenseAllNodesAsync(int maxSourceMemories = 200);
}
