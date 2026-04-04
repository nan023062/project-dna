using System.ComponentModel;
using System.Text.Json;
using Dna.Workbench.Contracts;
using Dna.Workbench.Governance;
using Dna.Workbench.Tasks;
using Dna.Knowledge;
using Dna.Knowledge.Workspace.Models;
using Dna.Memory.Models;
using ModelContextProtocol.Server;

namespace Dna.App.Interfaces.Mcp;

[McpServerToolType]
public sealed class WorkbenchTools(IWorkbenchFacade workbench)
{
    private static readonly JsonSerializerOptions PrettyJson = new() { WriteIndented = true };

    [McpServerTool, Description("Return the current topology snapshot for the active workspace.")]
    public Task<string> get_topology()
    {
        try
        {
            var result = workbench.Knowledge.GetTopologySnapshot();
            return Task.FromResult(JsonSerializer.Serialize(result, PrettyJson));
        }
        catch (Exception ex)
        {
            return Task.FromResult($"Error: {ex.Message}");
        }
    }

    [McpServerTool, Description("Return the workspace directory snapshot for the active workspace.")]
    public Task<string> get_workspace_snapshot(
        [Description("Optional workspace-relative path.")] string? relativePath = null)
    {
        try
        {
            var result = workbench.Knowledge.GetWorkspaceSnapshot(relativePath);
            return Task.FromResult(JsonSerializer.Serialize(result, PrettyJson));
        }
        catch (Exception ex)
        {
            return Task.FromResult($"Error: {ex.Message}");
        }
    }

    [McpServerTool, Description("Return knowledge for a topology module.")]
    public Task<string> get_module_knowledge(
        [Description("Module node id or module name.")] string nodeIdOrName)
    {
        try
        {
            var result = workbench.Knowledge.GetModuleKnowledge(nodeIdOrName);
            if (result == null)
                return Task.FromResult($"Error: Module knowledge not found: {nodeIdOrName}");
            return Task.FromResult(JsonSerializer.Serialize(result, PrettyJson));
        }
        catch (Exception ex)
        {
            return Task.FromResult($"Error: {ex.Message}");
        }
    }

    [McpServerTool, Description("Create a new memory entry.")]
    public async Task<string> remember(
        [Description("Memory content.")] string content,
        [Description("Memory type (Structural/Semantic/Episodic/Working/Procedural).")] string type,
        [Description("Discipline tags, comma separated.")] string disciplines,
        [Description("Related node type (Project/Department/Technical/Team).")] string nodeType,
        [Description("Optional memory stage (ShortTerm/LongTerm).")] string? stage = null,
        [Description("Optional tags, comma separated.")] string? tags = null,
        [Description("Optional summary.")] string? summary = null,
        [Description("Optional features, comma separated.")] string? features = null,
        [Description("Optional related node id.")] string? nodeId = null,
        [Description("Optional parent memory id.")] string? parentId = null,
        [Description("Importance between 0 and 1.")] double importance = 0.5)
    {
        try
        {
            if (!Enum.TryParse<MemoryType>(type, true, out var memoryType))
                return $"Error: invalid memory type '{type}'.";
            if (!Enum.TryParse<Dna.Knowledge.NodeType>(nodeType, true, out var parsedNodeType))
                return $"Error: invalid node type '{nodeType}'.";
            MemoryStage? parsedStage = null;
            if (!string.IsNullOrWhiteSpace(stage))
            {
                if (!Enum.TryParse<MemoryStage>(stage, true, out var s))
                    return $"Error: invalid memory stage '{stage}'.";
                parsedStage = s;
            }

            var request = new RememberRequest
            {
                Content = content,
                Type = memoryType,
                NodeType = parsedNodeType,
                Source = MemorySource.Ai,
                Summary = summary,
                Disciplines = SplitCsv(disciplines),
                Features = SplitCsv(features).Count > 0 ? SplitCsv(features) : null,
                NodeId = nodeId,
                Tags = SplitCsv(tags),
                ParentId = parentId,
                Stage = parsedStage,
                Importance = Math.Clamp(importance, 0, 1)
            };

            var result = await workbench.Knowledge.RememberAsync(request);
            return $"Memory created [{result.Id}].";
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool, Description("Recall memory entries semantically.")]
    public async Task<string> recall(
        [Description("Natural language question.")] string question,
        [Description("Optional discipline filter, comma separated.")] string? disciplines = null,
        [Description("Optional feature filter, comma separated.")] string? features = null,
        [Description("Optional related node id.")] string? nodeId = null,
        [Description("Optional tag filter, comma separated.")] string? tags = null,
        [Description("Optional node type filter, comma separated.")] string? nodeTypes = null,
        [Description("Expand the constraint chain.")] bool expandConstraintChain = true,
        [Description("Maximum recall result count.")] int maxResults = 10)
    {
        try
        {
            var parsedNodeTypes = SplitCsv(nodeTypes)
                .Select(v => Enum.TryParse<Dna.Knowledge.NodeType>(v, true, out var parsed) ? (Dna.Knowledge.NodeType?)parsed : null)
                .Where(v => v.HasValue)
                .Select(v => v!.Value)
                .ToList();

            var request = new RecallQuery
            {
                Question = question,
                Disciplines = SplitCsv(disciplines).Count > 0 ? SplitCsv(disciplines) : null,
                Features = SplitCsv(features).Count > 0 ? SplitCsv(features) : null,
                NodeId = nodeId,
                Tags = SplitCsv(tags).Count > 0 ? SplitCsv(tags) : null,
                NodeTypes = parsedNodeTypes.Count > 0 ? parsedNodeTypes : null,
                ExpandConstraintChain = expandConstraintChain,
                MaxResults = maxResults
            };

            var result = await workbench.Knowledge.RecallAsync(request);
            return JsonSerializer.Serialize(result, PrettyJson);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool, Description("Return the current runtime projection snapshot.")]
    public Task<string> get_runtime_projection()
    {
        try
        {
            var result = workbench.Runtime.GetProjectionSnapshot();
            return Task.FromResult(JsonSerializer.Serialize(result, PrettyJson));
        }
        catch (Exception ex)
        {
            return Task.FromResult($"Error: {ex.Message}");
        }
    }

    [McpServerTool, Description("Resolve requirement text into module candidates.")]
    public async Task<string> resolve_requirement_support(
        [Description("Requirement text or hint.")] string requirementText,
        [Description("Max candidates to return.")] int maxCandidates = 10)
    {
        try
        {
            var request = new WorkbenchRequirementRequest
            {
                RequirementText = requirementText,
                MaxCandidates = maxCandidates
            };
            var result = await workbench.Tasks.ResolveRequirementSupportAsync(request);
            return JsonSerializer.Serialize(result, PrettyJson);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool, Description("Start a module task and acquire a lock.")]
    public async Task<string> start_task(
        [Description("Target module ID or name.")] string moduleIdOrName,
        [Description("Agent identifier.")] string agentId,
        [Description("Task type (Requirement or Governance).")] string type,
        [Description("Task goal.")] string goal,
        [Description("Prerequisite task IDs, comma separated.")] string? prerequisiteTaskIds = null)
    {
        try
        {
            if (!Enum.TryParse<WorkbenchTaskType>(type, true, out var taskType))
                return $"Error: invalid task type '{type}'.";

            var request = new WorkbenchTaskRequest
            {
                ModuleIdOrName = moduleIdOrName,
                AgentId = agentId,
                Type = taskType,
                Goal = goal,
                PrerequisiteTaskIds = SplitCsv(prerequisiteTaskIds)
            };
            var result = await workbench.Tasks.StartTaskAsync(request);
            return JsonSerializer.Serialize(result, PrettyJson);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool, Description("End a module task and release the lock.")]
    public async Task<string> end_task(
        [Description("Task ID.")] string taskId,
        [Description("Task outcome (Success, Failed, Blocked).")] string outcome,
        [Description("Task summary.")] string summary,
        [Description("Decisions made, comma separated.")] string? decisions = null,
        [Description("Lessons learned, comma separated.")] string? lessons = null,
        [Description("Pending dependencies, comma separated.")] string? pendingDependencies = null)
    {
        try
        {
            if (!Enum.TryParse<WorkbenchTaskOutcome>(outcome, true, out var taskOutcome))
                return $"Error: invalid task outcome '{outcome}'.";

            var request = new WorkbenchTaskResult
            {
                TaskId = taskId,
                Outcome = taskOutcome,
                Summary = summary,
                Decisions = SplitCsv(decisions),
                Lessons = SplitCsv(lessons),
                PendingDependencies = SplitCsv(pendingDependencies)
            };
            var result = await workbench.Tasks.EndTaskAsync(request);
            return JsonSerializer.Serialize(result, PrettyJson);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool, Description("List currently active tasks.")]
    public async Task<string> list_active_tasks()
    {
        try
        {
            var result = await workbench.Tasks.ListActiveTasksAsync();
            return JsonSerializer.Serialize(result, PrettyJson);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool, Description("List recently completed tasks.")]
    public async Task<string> list_completed_tasks(
        [Description("Max tasks to return.")] int limit = 50)
    {
        try
        {
            var result = await workbench.Tasks.ListCompletedTasksAsync(limit);
            return JsonSerializer.Serialize(result, PrettyJson);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool, Description("Resolve governance scope and return context.")]
    public async Task<string> resolve_governance(
        [Description("Governance cadence (HighFrequency, LowFrequency).")] string cadence = "HighFrequency",
        [Description("Governance scope kind (ActiveChanges, Module, Subtree, Global).")] string scope = "ActiveChanges",
        [Description("Scope node ID or name.")] string? nodeIdOrName = null,
        [Description("Include direct dependencies.")] bool includeDirectDependencies = true)
    {
        try
        {
            if (!Enum.TryParse<GovernanceCadence>(cadence, true, out var govCadence))
                return $"Error: invalid cadence '{cadence}'.";
            if (!Enum.TryParse<GovernanceScopeKind>(scope, true, out var govScope))
                return $"Error: invalid scope '{scope}'.";

            var request = new WorkbenchGovernanceRequest
            {
                Cadence = govCadence,
                Scope = govScope,
                NodeIdOrName = nodeIdOrName,
                IncludeDirectDependencies = includeDirectDependencies
            };
            var result = await workbench.Governance.ResolveGovernanceAsync(request);
            return JsonSerializer.Serialize(result, PrettyJson);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    private static List<string> SplitCsv(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? []
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}