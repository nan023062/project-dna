using System.Collections.Concurrent;

namespace Dna.Workbench.Tasks;

internal sealed class InMemoryModuleLockManager : IModuleLockManager
{
    private readonly ConcurrentDictionary<string, ModuleLock> _locks = new(StringComparer.OrdinalIgnoreCase);

    public bool TryAcquireLock(string moduleId, string taskId, string agentId, out ModuleLock moduleLock)
    {
        moduleLock = new ModuleLock
        {
            ModuleId = moduleId,
            TaskId = taskId,
            AgentId = agentId,
            AcquiredAtUtc = DateTime.UtcNow
        };

        if (_locks.TryAdd(moduleId, moduleLock))
            return true;

        moduleLock = _locks[moduleId];
        return false;
    }

    public bool ReleaseLock(string moduleId, string taskId)
    {
        if (!_locks.TryGetValue(moduleId, out var current))
            return false;

        if (!string.Equals(current.TaskId, taskId, StringComparison.OrdinalIgnoreCase))
            return false;

        return _locks.TryRemove(moduleId, out _);
    }

    public IReadOnlyList<ModuleLock> GetActiveLocks()
        => _locks.Values.OrderBy(item => item.AcquiredAtUtc).ToList();
}
