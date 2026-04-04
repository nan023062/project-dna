using System.Collections.Concurrent;
using System.Text.Json;
using Dna.Knowledge;
using Dna.Memory.Models;
using Dna.Workbench.Contracts;

namespace Dna.Workbench.Tasks;

internal sealed class WorkbenchTaskService(
    ITopoGraphApplicationService topology,
    IMemoryEngine memory,
    ITaskContextBuilder contextBuilder,
    IModuleLockManager lockManager) : IWorkbenchTaskService
{
    private readonly ConcurrentDictionary<string, ActiveTaskRecord> _activeTasks = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<WorkbenchCompletedTaskSnapshot> _completedTasks = new();
    private const int MaxCompletedTaskHistory = 200;

    // 这里故意只做确定性的字符串/元数据命中。
    // Workbench 不拥有大模型能力，也不应该替 Agent 做语义理解或任务规划。
    public Task<IReadOnlyList<WorkbenchTaskCandidate>> ResolveRequirementSupportAsync(
        WorkbenchRequirementRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        topology.BuildTopology();
        var snapshot = topology.GetWorkbenchSnapshot();
        var query = request.RequirementText?.Trim();
        if (string.IsNullOrWhiteSpace(query))
            return Task.FromResult<IReadOnlyList<WorkbenchTaskCandidate>>([]);

        var terms = query.Split([' ', '/', '-', '_'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var candidates = snapshot.Modules
            .Select(module => new
            {
                Module = module,
                Score = ScoreModule(module, query, terms)
            })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.Module.ArchitectureLayerScore)
            .ThenBy(item => item.Module.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(request.MaxCandidates, 1, 50))
            .Select(item => new WorkbenchTaskCandidate
            {
                ModuleId = item.Module.NodeId,
                ModuleName = item.Module.DisplayName,
                Summary = item.Module.Summary,
                ArchitectureLayerScore = item.Module.ArchitectureLayerScore,
                Dependencies = item.Module.Dependencies,
                Evidence = BuildCandidateEvidence(item.Module, query, terms)
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<WorkbenchTaskCandidate>>(candidates);
    }

    public async Task<WorkbenchTaskStartResponse> StartTaskAsync(
        WorkbenchTaskRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            topology.BuildTopology();

            var target = topology.GetModuleKnowledge(request.ModuleIdOrName)
                ?? throw new WorkbenchTaskStartException(
                    WorkbenchTaskErrorKind.ModuleNotFound,
                    $"Module not found: {request.ModuleIdOrName}",
                    moduleIdOrName: request.ModuleIdOrName);
            var prerequisiteStatuses = ResolvePrerequisiteStatuses(request.PrerequisiteTaskIds);
            var unsatisfied = prerequisiteStatuses.Where(item => !item.IsSatisfied).ToList();
            if (unsatisfied.Count > 0)
            {
                throw new WorkbenchTaskStartException(
                    WorkbenchTaskErrorKind.PrerequisitesNotSatisfied,
                    $"Prerequisite tasks are not satisfied: {string.Join(", ", unsatisfied.Select(item => item.TaskId))}",
                    moduleIdOrName: target.NodeId,
                    prerequisiteStatuses: prerequisiteStatuses);
            }

            var taskId = $"task_{Guid.NewGuid():N}";
            if (!lockManager.TryAcquireLock(target.NodeId, taskId, request.AgentId, out var lease))
            {
                throw new WorkbenchTaskStartException(
                    WorkbenchTaskErrorKind.ModuleLocked,
                    $"Module is already locked: {target.NodeId}",
                    moduleIdOrName: target.NodeId,
                    conflictingTaskId: lease.TaskId,
                    conflictingAgentId: lease.AgentId);
            }

            var normalizedRequest = new WorkbenchTaskRequest
            {
                ModuleIdOrName = target.NodeId,
                AgentId = request.AgentId,
                Type = request.Type,
                Goal = request.Goal,
                PrerequisiteTaskIds = request.PrerequisiteTaskIds
            };

            var context = await contextBuilder.BuildAsync(normalizedRequest, lease, cancellationToken);
            _activeTasks[taskId] = new ActiveTaskRecord(
                target.NodeId,
                target.Name,
                request.AgentId,
                request.Type,
                request.Goal,
                DateTime.UtcNow,
                request.PrerequisiteTaskIds);
            return new WorkbenchTaskStartResponse
            {
                Success = true,
                Context = new WorkbenchTaskContext
                {
                    TaskId = context.TaskId,
                    AgentId = context.AgentId,
                    Type = context.Type,
                    Goal = context.Goal,
                    ModuleId = context.ModuleId,
                    ModuleName = context.ModuleName,
                    WorkspaceBoundary = context.WorkspaceBoundary,
                    WorkspaceScope = context.WorkspaceScope,
                    TargetModule = context.TargetModule,
                    VisibleModules = context.VisibleModules,
                    OutgoingRelations = context.OutgoingRelations,
                    IncomingRelations = context.IncomingRelations,
                    CollaborationContexts = context.CollaborationContexts,
                    RelevantMemories = context.RelevantMemories,
                    PrerequisiteStatuses = prerequisiteStatuses,
                    Lease = context.Lease
                }
            };
        }
        catch (WorkbenchTaskStartException ex)
        {
            return new WorkbenchTaskStartResponse
            {
                Success = false,
                Error = new WorkbenchTaskOperationError
                {
                    Kind = ex.Kind,
                    Message = ex.Message,
                    ModuleIdOrName = ex.ModuleIdOrName,
                    ConflictingTaskId = ex.ConflictingTaskId,
                    ConflictingAgentId = ex.ConflictingAgentId,
                    PrerequisiteStatuses = ex.PrerequisiteStatuses
                }
            };
        }
        catch
        {
            throw;
        }
    }

    public async Task<WorkbenchTaskEndResponse> EndTaskAsync(
        WorkbenchTaskResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_activeTasks.TryRemove(result.TaskId, out var active))
        {
            return new WorkbenchTaskEndResponse
            {
                Success = false,
                Error = new WorkbenchTaskOperationError
                {
                    Kind = WorkbenchTaskErrorKind.TaskNotFound,
                    Message = $"Active task not found: {result.TaskId}"
                }
            };
        }

        var lockReleased = false;
        WorkbenchTaskCompletion? completion = null;
        try
        {
            await memory.RememberAsync(new RememberRequest
            {
                Type = MemoryType.Episodic,
                NodeType = NodeType.Technical,
                Source = MemorySource.Ai,
                NodeId = active.ModuleId,
                Disciplines = ["engineering"],
                Tags = BuildResultTags(result.Outcome),
                Summary = result.Summary,
                Content = BuildTaskSummaryContent(active, result),
                Stage = MemoryStage.ShortTerm,
                Importance = result.Outcome == WorkbenchTaskOutcome.Success ? 0.8 : 0.7
            });

            foreach (var decision in result.Decisions.Where(item => !string.IsNullOrWhiteSpace(item)))
            {
                await memory.RememberAsync(new RememberRequest
                {
                    Type = MemoryType.Structural,
                    NodeType = NodeType.Technical,
                    Source = MemorySource.Ai,
                    NodeId = active.ModuleId,
                    Disciplines = ["engineering"],
                    Tags = ["#decision", "#task-result"],
                    Summary = decision,
                    Content = decision,
                    Stage = MemoryStage.LongTerm,
                    Importance = 0.85
                });
            }

            foreach (var lesson in result.Lessons.Where(item => !string.IsNullOrWhiteSpace(item)))
            {
                await memory.RememberAsync(new RememberRequest
                {
                    Type = MemoryType.Episodic,
                    NodeType = NodeType.Technical,
                    Source = MemorySource.Ai,
                    NodeId = active.ModuleId,
                    Disciplines = ["engineering"],
                    Tags = [WellKnownTags.Lesson, "#task-result"],
                    Summary = lesson,
                    Content = JsonSerializer.Serialize(new LessonPayload
                    {
                        Title = lesson,
                        Context = result.Summary,
                        Resolution = string.Join("；", result.PendingDependencies),
                        Tags = ["task-result"]
                    }),
                    Stage = MemoryStage.LongTerm,
                    Importance = 0.8
                });
            }

            var completed = new WorkbenchCompletedTaskSnapshot
            {
                TaskId = result.TaskId,
                ModuleId = active.ModuleId,
                ModuleName = active.ModuleName,
                AgentId = active.AgentId,
                Type = active.Type,
                Outcome = result.Outcome,
                Summary = result.Summary,
                CompletedAtUtc = DateTime.UtcNow
            };

            _completedTasks.Enqueue(completed);
            while (_completedTasks.Count > MaxCompletedTaskHistory && _completedTasks.TryDequeue(out _))
            {
            }

            completion = new WorkbenchTaskCompletion
            {
                TaskId = result.TaskId,
                ModuleId = active.ModuleId,
                ModuleName = active.ModuleName,
                Outcome = result.Outcome,
                PendingDependencies = result.PendingDependencies
            };
        }
        finally
        {
            lockReleased = lockManager.ReleaseLock(active.ModuleId, result.TaskId);
        }

        return new WorkbenchTaskEndResponse
        {
            Success = true,
            Completion = new WorkbenchTaskCompletion
            {
                TaskId = completion!.TaskId,
                ModuleId = completion.ModuleId,
                ModuleName = completion.ModuleName,
                Outcome = completion.Outcome,
                LockReleased = lockReleased,
                PendingDependencies = completion.PendingDependencies
            }
        };
    }

    public Task<IReadOnlyList<WorkbenchActiveTaskSnapshot>> ListActiveTasksAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var snapshots = _activeTasks
            .Select(pair => new WorkbenchActiveTaskSnapshot
            {
                TaskId = pair.Key,
                ModuleId = pair.Value.ModuleId,
                ModuleName = pair.Value.ModuleName,
                AgentId = pair.Value.AgentId,
                Type = pair.Value.Type,
                Goal = pair.Value.Goal,
                StartedAtUtc = pair.Value.StartedAtUtc,
                PrerequisiteTaskIds = pair.Value.PrerequisiteTaskIds
            })
            .OrderBy(item => item.StartedAtUtc)
            .ToList();

        return Task.FromResult<IReadOnlyList<WorkbenchActiveTaskSnapshot>>(snapshots);
    }

    public Task<IReadOnlyList<WorkbenchCompletedTaskSnapshot>> ListCompletedTasksAsync(
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var snapshots = _completedTasks
            .Reverse()
            .Take(Math.Clamp(limit, 1, MaxCompletedTaskHistory))
            .ToList();

        return Task.FromResult<IReadOnlyList<WorkbenchCompletedTaskSnapshot>>(snapshots);
    }

    private static double ScoreModule(TopologyWorkbenchModuleView module, string query, string[] terms)
    {
        var score = 0d;

        if (ContainsIgnoreCase(module.DisplayName, query) || ContainsIgnoreCase(module.Name, query))
            score += 10;

        if (ContainsIgnoreCase(module.Summary, query))
            score += 6;

        foreach (var term in terms)
        {
            if (ContainsIgnoreCase(module.DisplayName, term) || ContainsIgnoreCase(module.Name, term))
                score += 3;

            if (ContainsIgnoreCase(module.Summary, term))
                score += 2;

            if (module.Keywords.Any(keyword => ContainsIgnoreCase(keyword, term)))
                score += 1.5;
        }

        return score;
    }

    private static List<string> BuildCandidateEvidence(TopologyWorkbenchModuleView module, string query, string[] terms)
    {
        var evidence = new List<string>();

        if (ContainsIgnoreCase(module.DisplayName, query) || ContainsIgnoreCase(module.Name, query))
            evidence.Add("模块名与输入文本直接命中");

        if (ContainsIgnoreCase(module.Summary, query))
            evidence.Add("模块摘要与输入文本直接命中");

        if (terms.Any(term => module.Keywords.Any(keyword => ContainsIgnoreCase(keyword, term))))
            evidence.Add("模块关键词与输入文本命中");

        if (evidence.Count == 0)
            evidence.Add("模块与输入文本存在弱匹配，建议由 Agent 或人工确认");

        return evidence;
    }

    private static bool ContainsIgnoreCase(string? content, string term)
        => !string.IsNullOrWhiteSpace(content) &&
           content.Contains(term, StringComparison.OrdinalIgnoreCase);

    private List<WorkbenchTaskDependencyStatus> ResolvePrerequisiteStatuses(IReadOnlyList<string> prerequisiteTaskIds)
    {
        if (prerequisiteTaskIds.Count == 0)
            return [];

        var completed = _completedTasks.ToList();
        return prerequisiteTaskIds
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(taskId =>
            {
                var finished = completed.LastOrDefault(item =>
                    string.Equals(item.TaskId, taskId, StringComparison.OrdinalIgnoreCase));
                return new WorkbenchTaskDependencyStatus
                {
                    TaskId = taskId,
                    IsSatisfied = finished is { Outcome: WorkbenchTaskOutcome.Success },
                    Outcome = finished?.Outcome,
                    Summary = finished?.Summary
                };
            })
            .ToList();
    }

    private static List<string> BuildResultTags(WorkbenchTaskOutcome outcome)
    {
        return outcome switch
        {
            WorkbenchTaskOutcome.Success => ["#completed-task", "#task-result"],
            WorkbenchTaskOutcome.Blocked => [WellKnownTags.ActiveTask, "#task-result", "#blocked"],
            _ => ["#task-result", "#failed"]
        };
    }

    private static string BuildTaskSummaryContent(ActiveTaskRecord active, WorkbenchTaskResult result)
    {
        if (result.Outcome == WorkbenchTaskOutcome.Blocked)
        {
            return JsonSerializer.Serialize(new ActiveTaskPayload
            {
                Task = active.Goal,
                Status = "blocked",
                Assignee = active.AgentId,
                RelatedModules = [active.ModuleId],
                Notes = result.Summary
            });
        }

        return $"""
                TaskId: {result.TaskId}
                Module: {active.ModuleName} ({active.ModuleId})
                Agent: {active.AgentId}
                Type: {active.Type}
                Goal: {active.Goal}
                Outcome: {result.Outcome}
                Summary: {result.Summary}
                PendingDependencies: {string.Join(", ", result.PendingDependencies)}
                """;
    }

    private sealed record ActiveTaskRecord(
        string ModuleId,
        string ModuleName,
        string AgentId,
        WorkbenchTaskType Type,
        string Goal,
        DateTime StartedAtUtc,
        IReadOnlyList<string> PrerequisiteTaskIds);
}
