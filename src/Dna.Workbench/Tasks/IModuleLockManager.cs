namespace Dna.Workbench.Tasks;

internal interface IModuleLockManager
{
    bool TryAcquireLock(string moduleId, string taskId, string agentId, out ModuleLock moduleLock);
    bool ReleaseLock(string moduleId, string taskId);
    IReadOnlyList<ModuleLock> GetActiveLocks();
}
