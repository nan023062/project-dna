using System.Text.Json;
using Dna.Knowledge;
using Dna.Memory.Models;
using Dna.Workbench.Contracts;

namespace Dna.Workbench.Tooling;

internal sealed class WorkbenchToolService(
    IKnowledgeWorkbenchService knowledge,
    IWorkbenchRuntimeService runtime) : IWorkbenchToolService
{
    private static readonly IReadOnlyList<WorkbenchToolDescriptor> ToolCatalog =
    [
        new()
        {
            Name = WorkbenchToolConstants.ToolNames.GetTopology,
            Group = WorkbenchToolConstants.Groups.Knowledge,
            Description = "Return the current topology snapshot for the active workspace.",
            ReadOnly = true
        },
        new()
        {
            Name = WorkbenchToolConstants.ToolNames.GetWorkspaceSnapshot,
            Group = WorkbenchToolConstants.Groups.Knowledge,
            Description = "Return the workspace directory snapshot for the active workspace.",
            ReadOnly = true,
            Parameters =
            [
                new()
                {
                    Name = "relativePath",
                    Type = "string?",
                    Required = false,
                    Description = "Optional workspace-relative path."
                }
            ]
        },
        new()
        {
            Name = WorkbenchToolConstants.ToolNames.GetModuleKnowledge,
            Group = WorkbenchToolConstants.Groups.Knowledge,
            Description = "Return knowledge for a topology module.",
            ReadOnly = true,
            Parameters =
            [
                new()
                {
                    Name = "nodeIdOrName",
                    Type = "string",
                    Required = true,
                    Description = "Module node id or module name."
                }
            ]
        },
        new()
        {
            Name = WorkbenchToolConstants.ToolNames.SaveModuleKnowledge,
            Group = WorkbenchToolConstants.Groups.Knowledge,
            Description = "Persist knowledge for a topology module.",
            ReadOnly = false,
            Parameters =
            [
                new()
                {
                    Name = "nodeIdOrName",
                    Type = "string",
                    Required = true,
                    Description = "Module node id or module name."
                },
                new()
                {
                    Name = "knowledge",
                    Type = "NodeKnowledge",
                    Required = true,
                    Description = "Knowledge document payload."
                }
            ]
        },
        new()
        {
            Name = WorkbenchToolConstants.ToolNames.Remember,
            Group = WorkbenchToolConstants.Groups.Memory,
            Description = "Create a new memory entry.",
            ReadOnly = false,
            Parameters =
            [
                new() { Name = "content", Type = "string", Required = true, Description = "Memory content." },
                new() { Name = "type", Type = "MemoryType", Required = true, Description = "Memory type." },
                new() { Name = "disciplines", Type = "List<string>", Required = true, Description = "Discipline tags." },
                new() { Name = "nodeType", Type = "NodeType", Required = true, Description = "Related node type." },
                new() { Name = "stage", Type = "MemoryStage?", Required = false, Description = "Optional memory stage." },
                new() { Name = "tags", Type = "List<string>", Required = false, Description = "Optional tags." },
                new() { Name = "summary", Type = "string?", Required = false, Description = "Optional summary." },
                new() { Name = "features", Type = "List<string>?", Required = false, Description = "Optional features." },
                new() { Name = "nodeId", Type = "string?", Required = false, Description = "Optional related node id." },
                new() { Name = "parentId", Type = "string?", Required = false, Description = "Optional parent memory id." },
                new() { Name = "importance", Type = "double", Required = false, Description = "Importance between 0 and 1." }
            ]
        },
        new()
        {
            Name = WorkbenchToolConstants.ToolNames.Recall,
            Group = WorkbenchToolConstants.Groups.Memory,
            Description = "Recall memory entries semantically.",
            ReadOnly = true,
            Parameters =
            [
                new() { Name = "question", Type = "string", Required = true, Description = "Natural language question." },
                new() { Name = "disciplines", Type = "List<string>?", Required = false, Description = "Optional discipline filter." },
                new() { Name = "features", Type = "List<string>?", Required = false, Description = "Optional feature filter." },
                new() { Name = "nodeId", Type = "string?", Required = false, Description = "Optional related node id." },
                new() { Name = "tags", Type = "List<string>?", Required = false, Description = "Optional tag filter." },
                new() { Name = "nodeTypes", Type = "List<NodeType>?", Required = false, Description = "Optional node type filter." },
                new() { Name = "expandConstraintChain", Type = "bool", Required = false, Description = "Expand the constraint chain." },
                new() { Name = "maxResults", Type = "int", Required = false, Description = "Maximum recall result count." }
            ]
        },
        new()
        {
            Name = WorkbenchToolConstants.ToolNames.GetRuntimeProjection,
            Group = WorkbenchToolConstants.Groups.Runtime,
            Description = "Return the current runtime projection snapshot.",
            ReadOnly = true
        }
    ];

    public IReadOnlyList<WorkbenchToolDescriptor> ListTools() => ToolCatalog;

    public WorkbenchToolDescriptor? FindTool(string name)
        => ToolCatalog.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));

    public async Task<WorkbenchToolInvocationResult> InvokeAsync(
        WorkbenchToolInvocationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var toolName = request.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(toolName))
            return Failure(toolName, "Tool name is required.");

        if (FindTool(toolName) is null)
            return Failure(toolName, $"Unknown workbench tool: {toolName}");

        try
        {
            return toolName switch
            {
                WorkbenchToolConstants.ToolNames.GetTopology => Success(toolName, knowledge.GetTopologySnapshot()),
                WorkbenchToolConstants.ToolNames.GetWorkspaceSnapshot => Success(
                    toolName,
                    knowledge.GetWorkspaceSnapshot(GetOptionalString(request.Arguments, "relativePath"))),
                WorkbenchToolConstants.ToolNames.GetModuleKnowledge => InvokeGetModuleKnowledge(request.Arguments),
                WorkbenchToolConstants.ToolNames.SaveModuleKnowledge => Success(
                    toolName,
                    knowledge.SaveModuleKnowledge(DeserializeArguments<TopologyModuleKnowledgeUpsertCommand>(request.Arguments))),
                WorkbenchToolConstants.ToolNames.Remember => Success(
                    toolName,
                    await knowledge.RememberAsync(
                        DeserializeArguments<RememberRequest>(request.Arguments),
                        cancellationToken)),
                WorkbenchToolConstants.ToolNames.Recall => Success(
                    toolName,
                    await knowledge.RecallAsync(
                        DeserializeArguments<RecallQuery>(request.Arguments),
                        cancellationToken)),
                WorkbenchToolConstants.ToolNames.GetRuntimeProjection => Success(toolName, runtime.GetProjectionSnapshot()),
                _ => Failure(toolName, $"Unknown workbench tool: {toolName}")
            };
        }
        catch (Exception ex)
        {
            return Failure(toolName, ex.Message);
        }
    }

    private WorkbenchToolInvocationResult InvokeGetModuleKnowledge(JsonElement arguments)
    {
        var nodeIdOrName = GetRequiredString(arguments, "nodeIdOrName");
        var view = knowledge.GetModuleKnowledge(nodeIdOrName);
        return view is null
            ? Failure(WorkbenchToolConstants.ToolNames.GetModuleKnowledge, $"Module knowledge not found: {nodeIdOrName}")
            : Success(WorkbenchToolConstants.ToolNames.GetModuleKnowledge, view);
    }

    private static T DeserializeArguments<T>(JsonElement arguments)
    {
        if (arguments.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            throw new InvalidOperationException($"Arguments are required for {typeof(T).Name}.");

        var result = arguments.Deserialize<T>();
        return result ?? throw new InvalidOperationException($"Failed to deserialize tool arguments as {typeof(T).Name}.");
    }

    private static string GetRequiredString(JsonElement arguments, string propertyName)
    {
        var value = GetOptionalString(arguments, propertyName);
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"Argument '{propertyName}' is required.")
            : value;
    }

    private static string? GetOptionalString(JsonElement arguments, string propertyName)
    {
        if (arguments.ValueKind != JsonValueKind.Object)
            return null;

        if (!arguments.TryGetProperty(propertyName, out var property))
            return null;

        return property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString();
    }

    private static WorkbenchToolInvocationResult Success(string toolName, object? payload)
    {
        return new WorkbenchToolInvocationResult
        {
            ToolName = toolName,
            Success = true,
            Payload = ToJsonElement(payload)
        };
    }

    private static WorkbenchToolInvocationResult Failure(string toolName, string error)
    {
        return new WorkbenchToolInvocationResult
        {
            ToolName = toolName,
            Success = false,
            Error = error,
            Payload = ToJsonElement(new { error })
        };
    }

    private static JsonElement ToJsonElement(object? value)
    {
        if (value is null)
            return JsonDocument.Parse("null").RootElement.Clone();

        return JsonSerializer.SerializeToElement(value, value.GetType());
    }
}
