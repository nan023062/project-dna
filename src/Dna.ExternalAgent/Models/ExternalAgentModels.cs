using Dna.Workbench.Tooling;

namespace Dna.ExternalAgent.Models;

public sealed class ExternalAgentAdapterDescriptor
{
    public string ProductId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string InstallMode { get; init; } = ExternalAgentConstants.InstallModes.ProjectFiles;
    public IReadOnlyList<string> Capabilities { get; init; } = [];
    public IReadOnlyList<string> ManagedPaths { get; init; } = [];
}

public sealed class ExternalAgentTopologyPolicy
{
    public bool RequireTopologyFirst { get; init; } = true;
    public bool RequireModuleResolution { get; init; } = true;
    public bool RequireContainmentNavigation { get; init; } = true;
    public bool RequireDependencyDirectionValidation { get; init; } = true;
    public bool RequireCollaborationValidation { get; init; } = true;
    public bool RequireMemoryWriteBack { get; init; } = true;
    public bool StrictMode { get; init; } = true;
    public IReadOnlyList<string> RequiredToolNames { get; init; } = [];
    public IReadOnlyList<string> WorkflowRules { get; init; } = [];
}

public sealed class ExternalAgentManagedFile
{
    public string RelativePath { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public string Purpose { get; init; } = string.Empty;
}

public sealed class ExternalAgentPackageRequest
{
    public string ProductId { get; init; } = string.Empty;
    public string ServerName { get; init; } = "agentic-os";
    public string McpEndpoint { get; init; } = "http://127.0.0.1:5052/mcp";
    public string? WorkspaceRoot { get; init; }
    public bool StrictTopologyMode { get; init; } = true;
}

public sealed class ExternalAgentPackageResult
{
    public ExternalAgentAdapterDescriptor Adapter { get; init; } = new();
    public ExternalAgentTopologyPolicy Policy { get; init; } = new();
    public IReadOnlyList<WorkbenchToolDescriptor> RequiredTools { get; init; } = [];
    public IReadOnlyList<ExternalAgentManagedFile> ManagedFiles { get; init; } = [];
    public IReadOnlyList<string> Notes { get; init; } = [];
}

public sealed class ExternalAgentPackageContext
{
    public required ExternalAgentPackageRequest Request { get; init; }
    public required ExternalAgentTopologyPolicy Policy { get; init; }
    public required IReadOnlyList<WorkbenchToolDescriptor> RequiredTools { get; init; }
    public required string SharedInstructionsMarkdown { get; init; }
}
