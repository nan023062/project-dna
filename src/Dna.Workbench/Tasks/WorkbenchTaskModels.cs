using Dna.Knowledge;
using Dna.Memory.Models;

namespace Dna.Workbench.Tasks;

public enum WorkbenchTaskType
{
    Requirement,
    Governance
}

public enum WorkbenchTaskOutcome
{
    Success,
    Failed,
    Blocked
}

public enum WorkbenchTaskErrorKind
{
    ModuleNotFound,
    ModuleLocked,
    PrerequisitesNotSatisfied,
    TaskNotFound
}

public sealed class WorkbenchTaskCandidate
{
    public string ModuleId { get; init; } = string.Empty;
    public string ModuleName { get; init; } = string.Empty;
    public string? Summary { get; init; }
    public int ArchitectureLayerScore { get; init; }
    public IReadOnlyList<string> Evidence { get; init; } = [];
    public IReadOnlyList<string> Dependencies { get; init; } = [];
}

public sealed class WorkbenchRequirementRequest
{
    public string RequirementText { get; init; } = string.Empty;
    public int MaxCandidates { get; init; } = 10;
}

public sealed class WorkbenchTaskRequest
{
    public string ModuleIdOrName { get; init; } = string.Empty;
    public string AgentId { get; init; } = string.Empty;
    public WorkbenchTaskType Type { get; init; } = WorkbenchTaskType.Requirement;
    public string Goal { get; init; } = string.Empty;
    public IReadOnlyList<string> PrerequisiteTaskIds { get; init; } = [];
}

public sealed class WorkbenchTaskContextModule
{
    public string NodeId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Summary { get; init; }
    public string? Boundary { get; init; }
    public IReadOnlyList<string> ManagedPaths { get; init; } = [];
    public IReadOnlyList<string> PublicApi { get; init; } = [];
    public IReadOnlyList<string> Constraints { get; init; } = [];
    public IReadOnlyList<string> DeclaredDependencies { get; init; } = [];
    public IReadOnlyList<string> ComputedDependencies { get; init; } = [];
    public string? Identity { get; init; }
    public IReadOnlyList<string> Facts { get; init; } = [];
    public IReadOnlyList<string> ActiveTasks { get; init; } = [];
}

public sealed class WorkbenchVisibleModuleContext
{
    public string NodeId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public ContextLevel Level { get; init; }
    public string? Summary { get; init; }
    public string? ContractContent { get; init; }
    public string? IdentityContent { get; init; }
    public string? LessonsContent { get; init; }
    public string? ActiveContent { get; init; }
    public string? Boundary { get; init; }
    public IReadOnlyList<string> ManagedPaths { get; init; } = [];
    public IReadOnlyList<string> PublicApi { get; init; } = [];
    public IReadOnlyList<string> Constraints { get; init; } = [];
}

public sealed class WorkbenchTaskRelation
{
    public string FromId { get; init; } = string.Empty;
    public string FromName { get; init; } = string.Empty;
    public string ToId { get; init; } = string.Empty;
    public string ToName { get; init; } = string.Empty;
    public TopologyRelationType Type { get; init; } = TopologyRelationType.Dependency;
    public bool IsComputed { get; init; }
    public string? Label { get; init; }
}

public sealed class WorkbenchTaskCrossWorkContext
{
    public string CrossWorkId { get; init; } = string.Empty;
    public string CrossWorkName { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? Feature { get; init; }
    public string? TargetRole { get; init; }
    public IReadOnlyList<WorkbenchTaskCrossWorkParticipant> Participants { get; init; } = [];
}

public sealed class WorkbenchTaskCrossWorkParticipant
{
    public string ModuleName { get; init; } = string.Empty;
    public string? ModuleId { get; init; }
    public string Role { get; init; } = string.Empty;
    public string? Contract { get; init; }
    public string? ContractType { get; init; }
    public string? Deliverable { get; init; }
}

public sealed class WorkbenchTaskContext
{
    public string TaskId { get; init; } = string.Empty;
    public string AgentId { get; init; } = string.Empty;
    public WorkbenchTaskType Type { get; init; } = WorkbenchTaskType.Requirement;
    public string Goal { get; init; } = string.Empty;
    public string ModuleId { get; init; } = string.Empty;
    public string ModuleName { get; init; } = string.Empty;
    public string WorkspaceBoundary { get; init; } = string.Empty;
    public WorkbenchTaskWorkspaceScope WorkspaceScope { get; init; } = new();
    public WorkbenchTaskContextModule TargetModule { get; init; } = new();
    public IReadOnlyList<WorkbenchVisibleModuleContext> VisibleModules { get; init; } = [];
    public IReadOnlyList<WorkbenchTaskRelation> OutgoingRelations { get; init; } = [];
    public IReadOnlyList<WorkbenchTaskRelation> IncomingRelations { get; init; } = [];
    public IReadOnlyList<WorkbenchTaskCrossWorkContext> CollaborationContexts { get; init; } = [];
    public IReadOnlyList<MemoryEntry> RelevantMemories { get; init; } = [];
    public IReadOnlyList<WorkbenchTaskDependencyStatus> PrerequisiteStatuses { get; init; } = [];
    public ModuleLock Lease { get; init; } = new();
}

public sealed class WorkbenchTaskWorkspaceScope
{
    public IReadOnlyList<WorkbenchTaskPathScope> WritableScopes { get; init; } = [];
    public IReadOnlyList<WorkbenchTaskPathScope> ReadableScopes { get; init; } = [];
    public IReadOnlyList<WorkbenchTaskContractScope> ContractOnlyScopes { get; init; } = [];
}

public sealed class WorkbenchTaskPathScope
{
    public string ModuleId { get; init; } = string.Empty;
    public string ModuleName { get; init; } = string.Empty;
    public IReadOnlyList<string> Paths { get; init; } = [];
}

public sealed class WorkbenchTaskContractScope
{
    public string ModuleId { get; init; } = string.Empty;
    public string ModuleName { get; init; } = string.Empty;
    public ContextLevel Level { get; init; }
    public string? ContractContent { get; init; }
    public IReadOnlyList<string> PublicApi { get; init; } = [];
    public IReadOnlyList<string> Constraints { get; init; } = [];
}

public sealed class WorkbenchTaskResult
{
    public string TaskId { get; init; } = string.Empty;
    public WorkbenchTaskOutcome Outcome { get; init; } = WorkbenchTaskOutcome.Success;
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyList<string> Decisions { get; init; } = [];
    public IReadOnlyList<string> Lessons { get; init; } = [];
    public IReadOnlyList<string> PendingDependencies { get; init; } = [];
}

public sealed class WorkbenchTaskDependencyStatus
{
    public string TaskId { get; init; } = string.Empty;
    public bool IsSatisfied { get; init; }
    public WorkbenchTaskOutcome? Outcome { get; init; }
    public string? Summary { get; init; }
}

public sealed class WorkbenchActiveTaskSnapshot
{
    public string TaskId { get; init; } = string.Empty;
    public string ModuleId { get; init; } = string.Empty;
    public string ModuleName { get; init; } = string.Empty;
    public string AgentId { get; init; } = string.Empty;
    public WorkbenchTaskType Type { get; init; } = WorkbenchTaskType.Requirement;
    public string Goal { get; init; } = string.Empty;
    public DateTime StartedAtUtc { get; init; } = DateTime.UtcNow;
    public IReadOnlyList<string> PrerequisiteTaskIds { get; init; } = [];
}

public sealed class WorkbenchCompletedTaskSnapshot
{
    public string TaskId { get; init; } = string.Empty;
    public string ModuleId { get; init; } = string.Empty;
    public string ModuleName { get; init; } = string.Empty;
    public string AgentId { get; init; } = string.Empty;
    public WorkbenchTaskType Type { get; init; } = WorkbenchTaskType.Requirement;
    public WorkbenchTaskOutcome Outcome { get; init; } = WorkbenchTaskOutcome.Success;
    public string Summary { get; init; } = string.Empty;
    public DateTime CompletedAtUtc { get; init; } = DateTime.UtcNow;
}

public sealed class WorkbenchTaskCompletion
{
    public string TaskId { get; init; } = string.Empty;
    public string ModuleId { get; init; } = string.Empty;
    public string ModuleName { get; init; } = string.Empty;
    public WorkbenchTaskOutcome Outcome { get; init; } = WorkbenchTaskOutcome.Success;
    public bool LockReleased { get; init; }
    public IReadOnlyList<string> PendingDependencies { get; init; } = [];
}

public sealed class WorkbenchTaskOperationError
{
    public WorkbenchTaskErrorKind Kind { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? ModuleIdOrName { get; init; }
    public string? ConflictingTaskId { get; init; }
    public string? ConflictingAgentId { get; init; }
    public IReadOnlyList<WorkbenchTaskDependencyStatus> PrerequisiteStatuses { get; init; } = [];
}

public sealed class WorkbenchTaskStartResponse
{
    public bool Success { get; init; }
    public WorkbenchTaskContext? Context { get; init; }
    public WorkbenchTaskOperationError? Error { get; init; }
}

public sealed class WorkbenchTaskEndResponse
{
    public bool Success { get; init; }
    public WorkbenchTaskCompletion? Completion { get; init; }
    public WorkbenchTaskOperationError? Error { get; init; }
}

public sealed class ModuleLock
{
    public string ModuleId { get; init; } = string.Empty;
    public string TaskId { get; init; } = string.Empty;
    public string AgentId { get; init; } = string.Empty;
    public DateTime AcquiredAtUtc { get; init; } = DateTime.UtcNow;
}

public sealed class WorkbenchTaskStartException : InvalidOperationException
{
    public WorkbenchTaskStartException(
        WorkbenchTaskErrorKind kind,
        string message,
        string? moduleIdOrName = null,
        string? conflictingTaskId = null,
        string? conflictingAgentId = null,
        IReadOnlyList<WorkbenchTaskDependencyStatus>? prerequisiteStatuses = null) : base(message)
    {
        Kind = kind;
        ModuleIdOrName = moduleIdOrName;
        ConflictingTaskId = conflictingTaskId;
        ConflictingAgentId = conflictingAgentId;
        PrerequisiteStatuses = prerequisiteStatuses ?? [];
    }

    public WorkbenchTaskErrorKind Kind { get; }
    public string? ModuleIdOrName { get; }
    public string? ConflictingTaskId { get; }
    public string? ConflictingAgentId { get; }
    public IReadOnlyList<WorkbenchTaskDependencyStatus> PrerequisiteStatuses { get; }
}
