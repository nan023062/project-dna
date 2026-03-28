namespace Dna.Knowledge;

public interface IGovernanceEngine
{
    GovernanceReport ValidateArchitecture();
    int CheckFreshness();
    int DetectMemoryConflicts();
    int ArchiveStaleMemories(TimeSpan staleThreshold);
    Task<KnowledgeCondenseResult> CondenseNodeKnowledgeAsync(string nodeIdOrName, int maxSourceMemories = 200);
    Task<List<KnowledgeCondenseResult>> CondenseAllNodesAsync(int maxSourceMemories = 200);
}
