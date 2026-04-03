namespace Dna.Knowledge;

public interface IGovernanceEngine
{
    GovernanceReport ValidateArchitecture();
    int CheckFreshness();
    int DetectMemoryConflicts();
    int ArchiveStaleMemories(TimeSpan staleThreshold);
    Task<KnowledgeEvolutionReport> EvolveKnowledgeAsync(string? nodeIdOrName = null, int maxSuggestions = 50);
    Task<KnowledgeCondenseResult> CondenseNodeKnowledgeAsync(string nodeIdOrName, int maxSourceMemories = 200);
    Task<List<KnowledgeCondenseResult>> CondenseAllNodesAsync(int maxSourceMemories = 200);
}
