namespace Dna.Knowledge;

public interface IGovernanceEngine
{
    GovernanceReport ValidateArchitecture();
    int CheckFreshness();
    int DetectMemoryConflicts();
    int ArchiveStaleMemories(TimeSpan staleThreshold);
}
