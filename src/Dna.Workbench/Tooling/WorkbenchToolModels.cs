using System.Text.Json;

namespace Dna.Workbench.Tooling;

public sealed class WorkbenchToolDescriptor
{
    public string Name { get; init; } = string.Empty;
    public string Group { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public bool ReadOnly { get; init; }
    public IReadOnlyList<WorkbenchToolParameterDescriptor> Parameters { get; init; } = [];
}

public sealed class WorkbenchToolParameterDescriptor
{
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public bool Required { get; init; }
    public string? Description { get; init; }
}

public sealed class WorkbenchToolInvocationContext
{
    public string SourceKind { get; init; } = "unknown";
    public string? SourceId { get; init; }
    public string? SessionId { get; init; }
    public string? WorkspaceRoot { get; init; }
    public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class WorkbenchToolInvocationRequest
{
    public string Name { get; init; } = string.Empty;
    public JsonElement Arguments { get; init; }
    public WorkbenchToolInvocationContext Context { get; init; } = new();
}

public sealed class WorkbenchToolInvocationResult
{
    public string ToolName { get; init; } = string.Empty;
    public bool Success { get; init; }
    public JsonElement Payload { get; init; }
    public string? Error { get; init; }
    public DateTime ExecutedAtUtc { get; init; } = DateTime.UtcNow;
}
