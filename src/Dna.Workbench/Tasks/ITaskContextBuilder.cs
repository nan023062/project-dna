namespace Dna.Workbench.Tasks;

internal interface ITaskContextBuilder
{
    Task<WorkbenchTaskContext> BuildAsync(
        WorkbenchTaskRequest request,
        ModuleLock lease,
        CancellationToken cancellationToken = default);
}
