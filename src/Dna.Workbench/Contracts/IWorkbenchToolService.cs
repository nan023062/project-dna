using Dna.Workbench.Tooling;

namespace Dna.Workbench.Contracts;

public interface IWorkbenchToolService
{
    IReadOnlyList<WorkbenchToolDescriptor> ListTools();

    WorkbenchToolDescriptor? FindTool(string name);

    Task<WorkbenchToolInvocationResult> InvokeAsync(
        WorkbenchToolInvocationRequest request,
        CancellationToken cancellationToken = default);
}
