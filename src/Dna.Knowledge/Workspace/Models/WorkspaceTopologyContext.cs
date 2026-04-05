namespace Dna.Knowledge.Workspace.Models;

public sealed class WorkspaceTopologyContext
{
    public List<string> ExcludeDirs { get; init; } = [];
    public List<WorkspaceModuleRegistration> Modules { get; init; } = [];
}

public sealed class WorkspaceModuleRegistration
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Discipline { get; init; } = string.Empty;
    public int Layer { get; init; }
    public bool IsCrossWorkModule { get; init; }
    public string Path { get; init; } = string.Empty;
    public List<string> ManagedPaths { get; init; } = [];
}
