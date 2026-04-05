using System.ComponentModel;
using System.Text.Json;
using Dna.Knowledge;
using Dna.Knowledge.Workspace.Models;
using Dna.Memory.Models;
using Dna.Workbench.Contracts;
using Dna.Workbench.Governance;
using Dna.Workbench.Tasks;
using Dna.Workbench.Tooling;
using ModelContextProtocol.Server;

namespace Dna.ExternalAgent.Interfaces.Mcp;

[McpServerToolType]
public sealed class ExternalAgentWorkbenchTools(IWorkbenchFacade workbench)
{
    private static readonly JsonSerializerOptions PrettyJson = new() { WriteIndented = true };

    [McpServerTool(Name = WorkbenchToolConstants.ToolNames.GetTopology, ReadOnly = true), Description("Return the current topology snapshot for the active workspace.")]
    public Task<string> get_topology()
    {
        try
        {
            var result = workbench.Knowledge.GetTopologySnapshot();
            return Task.FromResult(Success(nameof(get_topology), result));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Error(nameof(get_topology), "unexpected_error", ex.Message));
        }
    }

    [McpServerTool(Name = WorkbenchToolConstants.ToolNames.GetWorkspaceSnapshot, ReadOnly = true), Description("Return the workspace directory snapshot for the active workspace.")]
    public Task<string> get_workspace_snapshot(
        [Description("Optional workspace-relative path.")] string? relativePath = null)
    {
        try
        {
            var result = workbench.Knowledge.GetWorkspaceSnapshot(relativePath);
            return Task.FromResult(Success(nameof(get_workspace_snapshot), result));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Error(nameof(get_workspace_snapshot), "unexpected_error", ex.Message));
        }
    }

    [McpServerTool(Name = WorkbenchToolConstants.ToolNames.GetModuleKnowledge, ReadOnly = true), Description("Return knowledge for a topology module.")]
    public Task<string> get_module_knowledge(
        [Description("Module node id or module name.")] string nodeIdOrName)
    {
        try
        {
            var result = workbench.Knowledge.GetModuleKnowledge(nodeIdOrName);
            if (result == null)
                return Task.FromResult(Error(nameof(get_module_knowledge), "not_found", $"Module knowledge not found: {nodeIdOrName}"));

            return Task.FromResult(Success(nameof(get_module_knowledge), result));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Error(nameof(get_module_knowledge), "unexpected_error", ex.Message));
        }
    }

    [McpServerTool(Name = WorkbenchToolConstants.ToolNames.SaveModuleKnowledge), Description("Persist knowledge for a topology module.")]
    public Task<string> save_module_knowledge(
        [Description("Module node id or module name.")] string nodeIdOrName,
        [Description("Identity summary for the module.")] string? identity = null,
        [Description("Facts, comma separated.")] string? facts = null,
        [Description("Active task IDs, comma separated.")] string? activeTasks = null,
        [Description("Total memory count.")] int totalMemoryCount = 0,
        [Description("Identity memory ID.")] string? identityMemoryId = null,
        [Description("Upgrade trail memory ID.")] string? upgradeTrailMemoryId = null,
        [Description("Related memory IDs, comma separated.")] string? memoryIds = null)
    {
        try
        {
            var command = new TopologyModuleKnowledgeUpsertCommand
            {
                NodeIdOrName = nodeIdOrName,
                Knowledge = new NodeKnowledge
                {
                    Identity = identity,
                    Facts = SplitCsv(facts),
                    ActiveTasks = SplitCsv(activeTasks),
                    TotalMemoryCount = totalMemoryCount,
                    IdentityMemoryId = identityMemoryId,
                    UpgradeTrailMemoryId = upgradeTrailMemoryId,
                    MemoryIds = SplitCsv(memoryIds)
                }
            };

            var result = workbench.Knowledge.SaveModuleKnowledge(command);
            return Task.FromResult(Success(nameof(save_module_knowledge), result));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Error(nameof(save_module_knowledge), "unexpected_error", ex.Message));
        }
    }

    [McpServerTool(Name = WorkbenchToolConstants.ToolNames.Remember), Description("Create a new memory entry.")]
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
                return Error(nameof(remember), "validation_error", $"Invalid memory type '{type}'.");
            if (!Enum.TryParse<NodeType>(nodeType, true, out var parsedNodeType))
                return Error(nameof(remember), "validation_error", $"Invalid node type '{nodeType}'.");
            MemoryStage? parsedStage = null;
            if (!string.IsNullOrWhiteSpace(stage))
            {
                if (!Enum.TryParse<MemoryStage>(stage, true, out var parsed))
                    return Error(nameof(remember), "validation_error", $"Invalid memory stage '{stage}'.");
                parsedStage = parsed;
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
            return Success(nameof(remember), new
            {
                id = result.Id,
                summary = result.Summary,
                createdAt = result.CreatedAt,
                message = $"Memory created [{result.Id}]."
            });
        }
        catch (Exception ex)
        {
            return Error(nameof(remember), "unexpected_error", ex.Message);
        }
    }

    [McpServerTool(Name = WorkbenchToolConstants.ToolNames.Recall, ReadOnly = true), Description("Recall memory entries semantically.")]
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
                .Select(v => Enum.TryParse<NodeType>(v, true, out var parsed) ? (NodeType?)parsed : null)
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
            return Success(nameof(recall), result);
        }
        catch (Exception ex)
        {
            return Error(nameof(recall), "unexpected_error", ex.Message);
        }
    }

    [McpServerTool(Name = WorkbenchToolConstants.ToolNames.GetRuntimeProjection, ReadOnly = true), Description("Return the current runtime projection snapshot.")]
    public Task<string> get_runtime_projection()
    {
        try
        {
            var result = workbench.Runtime.GetProjectionSnapshot();
            return Task.FromResult(Success(nameof(get_runtime_projection), result));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Error(nameof(get_runtime_projection), "unexpected_error", ex.Message));
        }
    }

    [McpServerTool(Name = WorkbenchToolConstants.ToolNames.ResolveRequirementSupport, ReadOnly = true), Description("Resolve requirement text into module candidates.")]
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
            return Success(nameof(resolve_requirement_support), result);
        }
        catch (Exception ex)
        {
            return Error(nameof(resolve_requirement_support), "unexpected_error", ex.Message);
        }
    }

    [McpServerTool(Name = WorkbenchToolConstants.ToolNames.StartTask), Description("Start a module task and acquire a lock.")]
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
                return Error(nameof(start_task), "validation_error", $"Invalid task type '{type}'.");

            var request = new WorkbenchTaskRequest
            {
                ModuleIdOrName = moduleIdOrName,
                AgentId = agentId,
                Type = taskType,
                Goal = goal,
                PrerequisiteTaskIds = SplitCsv(prerequisiteTaskIds)
            };
            var result = await workbench.Tasks.StartTaskAsync(request);
            return Success(nameof(start_task), result);
        }
        catch (Exception ex)
        {
            return Error(nameof(start_task), "unexpected_error", ex.Message);
        }
    }

    [McpServerTool(Name = WorkbenchToolConstants.ToolNames.EndTask), Description("End a module task and release the lock.")]
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
                return Error(nameof(end_task), "validation_error", $"Invalid task outcome '{outcome}'.");

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
            return Success(nameof(end_task), result);
        }
        catch (Exception ex)
        {
            return Error(nameof(end_task), "unexpected_error", ex.Message);
        }
    }

    [McpServerTool(Name = WorkbenchToolConstants.ToolNames.ListActiveTasks, ReadOnly = true), Description("List currently active tasks.")]
    public async Task<string> list_active_tasks()
    {
        try
        {
            var result = await workbench.Tasks.ListActiveTasksAsync();
            return Success(nameof(list_active_tasks), result);
        }
        catch (Exception ex)
        {
            return Error(nameof(list_active_tasks), "unexpected_error", ex.Message);
        }
    }

    [McpServerTool(Name = WorkbenchToolConstants.ToolNames.ListCompletedTasks, ReadOnly = true), Description("List recently completed tasks.")]
    public async Task<string> list_completed_tasks(
        [Description("Max tasks to return.")] int limit = 50)
    {
        try
        {
            var result = await workbench.Tasks.ListCompletedTasksAsync(limit);
            return Success(nameof(list_completed_tasks), result);
        }
        catch (Exception ex)
        {
            return Error(nameof(list_completed_tasks), "unexpected_error", ex.Message);
        }
    }

    [McpServerTool(Name = WorkbenchToolConstants.ToolNames.ResolveGovernance, ReadOnly = true), Description("Resolve governance scope and return context.")]
    public async Task<string> resolve_governance(
        [Description("Governance cadence (HighFrequency, LowFrequency).")] string cadence = "HighFrequency",
        [Description("Governance scope kind (ActiveChanges, Module, Subtree, Global).")] string scope = "ActiveChanges",
        [Description("Scope node ID or name.")] string? nodeIdOrName = null,
        [Description("Include direct dependencies.")] bool includeDirectDependencies = true)
    {
        try
        {
            if (!Enum.TryParse<GovernanceCadence>(cadence, true, out var govCadence))
                return Error(nameof(resolve_governance), "validation_error", $"Invalid cadence '{cadence}'.");
            if (!Enum.TryParse<GovernanceScopeKind>(scope, true, out var govScope))
                return Error(nameof(resolve_governance), "validation_error", $"Invalid scope '{scope}'.");

            var request = new WorkbenchGovernanceRequest
            {
                Cadence = govCadence,
                Scope = govScope,
                NodeIdOrName = nodeIdOrName,
                IncludeDirectDependencies = includeDirectDependencies
            };
            var result = await workbench.Governance.ResolveGovernanceAsync(request);
            return Success(nameof(resolve_governance), result);
        }
        catch (Exception ex)
        {
            return Error(nameof(resolve_governance), "unexpected_error", ex.Message);
        }
    }

    private static string Success(string toolName, object? result)
        => JsonSerializer.Serialize(new
        {
            ok = true,
            tool = toolName,
            result
        }, PrettyJson);

    private static string Error(string toolName, string code, string message, object? details = null)
        => JsonSerializer.Serialize(new
        {
            ok = false,
            tool = toolName,
            error = new
            {
                code,
                message,
                details
            }
        }, PrettyJson);

    private static List<string> SplitCsv(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? []
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}
