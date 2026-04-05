using Dna.Core.Config;
using Dna.Knowledge;
using Dna.Memory.Models;
using Dna.Workbench.Contracts;
using Dna.Workbench.DependencyInjection;
using Dna.Workbench.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace App.Tests;

public sealed class WorkbenchTaskServiceTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"dna-workbench-tasks-{Guid.NewGuid():N}");
    private readonly string _workspaceRoot;
    private readonly string _metadataRoot;
    private readonly ServiceProvider _services;

    public WorkbenchTaskServiceTests()
    {
        _workspaceRoot = Path.Combine(_tempRoot, "workspace");
        _metadataRoot = Path.Combine(_workspaceRoot, ".agentic-os");

        Directory.CreateDirectory(_workspaceRoot);
        Directory.CreateDirectory(_metadataRoot);
        Directory.CreateDirectory(Path.Combine(_workspaceRoot, "src"));

        _services = new ServiceCollection()
            .AddLogging()
            .AddHttpClient()
            .AddSingleton(new Dna.App.Services.AppRuntimeOptions
            {
                ProjectName = "agentic-os-dev",
                WorkspaceRoot = _workspaceRoot,
                MetadataRootPath = _metadataRoot
            })
            .AddSingleton<ProjectConfig>()
            .AddKnowledgeGraph()
            .AddWorkbench()
            .BuildServiceProvider();

        var initializer = ActivatorUtilities.CreateInstance<Dna.App.Services.AppLocalRuntimeInitializer>(_services);
        initializer.StartAsync(CancellationToken.None).GetAwaiter().GetResult();

        SeedModules();
    }

    public void Dispose()
    {
        _services.Dispose();
        SqliteConnection.ClearAllPools();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(_tempRoot))
                    Directory.Delete(_tempRoot, recursive: true);

                break;
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(100);
            }
            catch (UnauthorizedAccessException) when (attempt < 4)
            {
                Thread.Sleep(100);
            }
        }
    }

    [Fact]
    public async Task ResolveRequirementSupportAsync_ShouldReturnMatchingModuleCandidates()
    {
        var tasks = _services.GetRequiredService<IWorkbenchTaskService>();

        var result = await tasks.ResolveRequirementSupportAsync(new WorkbenchRequirementRequest
        {
            RequirementText = "实现 core governance 能力",
            MaxCandidates = 5
        });

        Assert.NotEmpty(result);
        Assert.Equal("Core", result[0].ModuleName);
    }

    [Fact]
    public async Task StartTaskAsync_ShouldAcquireModuleLock_AndReturnVisibleDependencyContexts()
    {
        var tasks = _services.GetRequiredService<IWorkbenchTaskService>();

        var context = await tasks.StartTaskAsync(new WorkbenchTaskRequest
        {
            ModuleIdOrName = "Core",
            AgentId = "agent-1",
            Goal = "refactor governance task flow",
            Type = WorkbenchTaskType.Requirement
        });

        Assert.True(context.Success);
        Assert.NotNull(context.Context);
        Assert.False(string.IsNullOrWhiteSpace(context.Context!.TaskId));
        Assert.Equal("Core", context.Context.ModuleName);
        Assert.Equal("AgenticOs/Platform/Core", context.Context.ModuleId);
        Assert.Contains(context.Context.VisibleModules, item => item.Name == "Foundation");
        Assert.NotEmpty(context.Context.WorkspaceScope.WritableScopes);
        Assert.NotEmpty(context.Context.WorkspaceScope.ReadableScopes);
        Assert.NotEmpty(context.Context.WorkspaceScope.ContractOnlyScopes);
        Assert.Empty(context.Context.PrerequisiteStatuses);

        await tasks.EndTaskAsync(new WorkbenchTaskResult
        {
            TaskId = context.Context.TaskId,
            Outcome = WorkbenchTaskOutcome.Success,
            Summary = "completed"
        });
    }

    [Fact]
    public async Task StartTaskAsync_ShouldReject_WhenModuleAlreadyLocked()
    {
        var tasks = _services.GetRequiredService<IWorkbenchTaskService>();

        var first = await tasks.StartTaskAsync(new WorkbenchTaskRequest
        {
            ModuleIdOrName = "Core",
            AgentId = "agent-1",
            Goal = "task one"
        });
        Assert.True(first.Success);
        Assert.NotNull(first.Context);

        var second = await tasks.StartTaskAsync(new WorkbenchTaskRequest
        {
            ModuleIdOrName = "Core",
            AgentId = "agent-2",
            Goal = "task two"
        });
        Assert.False(second.Success);
        Assert.NotNull(second.Error);
        Assert.Equal(WorkbenchTaskErrorKind.ModuleLocked, second.Error!.Kind);
        Assert.Equal("AgenticOs/Platform/Core", second.Error.ModuleIdOrName);
        Assert.Equal(first.Context!.TaskId, second.Error.ConflictingTaskId);

        await tasks.EndTaskAsync(new WorkbenchTaskResult
        {
            TaskId = first.Context.TaskId,
            Outcome = WorkbenchTaskOutcome.Blocked,
            Summary = "blocked by test"
        });
    }

    [Fact]
    public async Task EndTaskAsync_ShouldPersistSummaryAndReleaseLock()
    {
        var tasks = _services.GetRequiredService<IWorkbenchTaskService>();
        var memory = _services.GetRequiredService<IMemoryEngine>();

        var context = await tasks.StartTaskAsync(new WorkbenchTaskRequest
        {
            ModuleIdOrName = "Core",
            AgentId = "agent-1",
            Goal = "finish module"
        });
        Assert.True(context.Success);
        Assert.NotNull(context.Context);

        var completion = await tasks.EndTaskAsync(new WorkbenchTaskResult
        {
            TaskId = context.Context!.TaskId,
            Outcome = WorkbenchTaskOutcome.Success,
            Summary = "core task completed",
            Decisions = ["将治理流程统一收口到任务服务"],
            Lessons = ["避免在 Workbench 中重复实现 Knowledge 的治理逻辑"]
        });

        Assert.True(completion.Success);
        Assert.NotNull(completion.Completion);
        Assert.True(completion.Completion!.LockReleased);
        Assert.Equal("AgenticOs/Platform/Core", completion.Completion.ModuleId);

        var entries = memory.QueryMemories(new MemoryFilter
        {
            NodeId = "AgenticOs/Platform/Core",
            Freshness = FreshnessFilter.All,
            Limit = 20
        });

        Assert.Contains(entries, item => item.Summary == "core task completed");
        Assert.Contains(entries, item => item.Summary == "将治理流程统一收口到任务服务");
        Assert.Contains(entries, item => item.Summary == "避免在 Workbench 中重复实现 Knowledge 的治理逻辑");

        var restarted = await tasks.StartTaskAsync(new WorkbenchTaskRequest
        {
            ModuleIdOrName = "Core",
            AgentId = "agent-2",
            Goal = "restart after release"
        });

        Assert.True(restarted.Success);
        Assert.NotNull(restarted.Context);
        Assert.False(string.IsNullOrWhiteSpace(restarted.Context!.TaskId));

        await tasks.EndTaskAsync(new WorkbenchTaskResult
        {
            TaskId = restarted.Context.TaskId,
            Outcome = WorkbenchTaskOutcome.Success,
            Summary = "restart completed"
        });
    }

    [Fact]
    public async Task StartTaskAsync_ShouldRequireSuccessfulPrerequisites()
    {
        var tasks = _services.GetRequiredService<IWorkbenchTaskService>();

        var first = await tasks.StartTaskAsync(new WorkbenchTaskRequest
        {
            ModuleIdOrName = "Foundation",
            AgentId = "agent-1",
            Goal = "prepare foundation"
        });
        Assert.True(first.Success);
        Assert.NotNull(first.Context);

        await tasks.EndTaskAsync(new WorkbenchTaskResult
        {
            TaskId = first.Context!.TaskId,
            Outcome = WorkbenchTaskOutcome.Success,
            Summary = "foundation prepared"
        });

        var second = await tasks.StartTaskAsync(new WorkbenchTaskRequest
        {
            ModuleIdOrName = "Core",
            AgentId = "agent-2",
            Goal = "use prepared foundation",
            PrerequisiteTaskIds = [first.Context.TaskId]
        });
        Assert.True(second.Success);
        Assert.NotNull(second.Context);

        var prerequisite = Assert.Single(second.Context!.PrerequisiteStatuses);
        Assert.Equal(first.Context.TaskId, prerequisite.TaskId);
        Assert.True(prerequisite.IsSatisfied);
        Assert.Equal(WorkbenchTaskOutcome.Success, prerequisite.Outcome);

        await tasks.EndTaskAsync(new WorkbenchTaskResult
        {
            TaskId = second.Context.TaskId,
            Outcome = WorkbenchTaskOutcome.Success,
            Summary = "core completed after prerequisite"
        });
    }

    [Fact]
    public async Task StartTaskAsync_ShouldRejectUnsatisfiedPrerequisites()
    {
        var tasks = _services.GetRequiredService<IWorkbenchTaskService>();

        var response = await tasks.StartTaskAsync(new WorkbenchTaskRequest
        {
            ModuleIdOrName = "Core",
            AgentId = "agent-1",
            Goal = "should fail",
            PrerequisiteTaskIds = ["task_missing"]
        });

        Assert.False(response.Success);
        Assert.NotNull(response.Error);
        Assert.Equal(WorkbenchTaskErrorKind.PrerequisitesNotSatisfied, response.Error!.Kind);
        Assert.Single(response.Error.PrerequisiteStatuses);
        Assert.False(response.Error.PrerequisiteStatuses[0].IsSatisfied);
    }

    [Fact]
    public async Task ListTaskSnapshotsAsync_ShouldExposeActiveAndCompletedTasks()
    {
        var tasks = _services.GetRequiredService<IWorkbenchTaskService>();

        var active = await tasks.StartTaskAsync(new WorkbenchTaskRequest
        {
            ModuleIdOrName = "Core",
            AgentId = "agent-1",
            Goal = "inspect snapshots"
        });
        Assert.True(active.Success);
        Assert.NotNull(active.Context);

        var activeSnapshots = await tasks.ListActiveTasksAsync();
        var activeSnapshot = Assert.Single(activeSnapshots);
        Assert.Equal(active.Context!.TaskId, activeSnapshot.TaskId);
        Assert.Equal("Core", activeSnapshot.ModuleName);

        await tasks.EndTaskAsync(new WorkbenchTaskResult
        {
            TaskId = active.Context.TaskId,
            Outcome = WorkbenchTaskOutcome.Success,
            Summary = "snapshot task done"
        });

        Assert.Empty(await tasks.ListActiveTasksAsync());

        var completedSnapshots = await tasks.ListCompletedTasksAsync();
        Assert.Contains(completedSnapshots, item => item.TaskId == active.Context.TaskId && item.Outcome == WorkbenchTaskOutcome.Success);
    }

    private void SeedModules()
    {
        var topology = _services.GetRequiredService<ITopoGraphApplicationService>();

        topology.RegisterModule("engineering", new TopologyModuleDefinition
        {
            Id = "AgenticOs/Platform",
            Name = "Platform",
            Path = "src/platform",
            Layer = 0,
            Summary = "Platform root"
        });

        topology.RegisterModule("engineering", new TopologyModuleDefinition
        {
            Id = "AgenticOs/Platform/Foundation",
            Name = "Foundation",
            Path = "src/foundation",
            Layer = 1,
            ParentModuleId = "AgenticOs/Platform",
            Summary = "Foundation module",
            ManagedPaths = ["src/foundation"],
            PublicApi = ["IFoundationApi"]
        });

        topology.RegisterModule("engineering", new TopologyModuleDefinition
        {
            Id = "AgenticOs/Platform/Core",
            Name = "Core",
            Path = "src/core",
            Layer = 2,
            ParentModuleId = "AgenticOs/Platform",
            Dependencies = ["Foundation"],
            Summary = "Core governance and workbench flows",
            ManagedPaths = ["src/core"],
            PublicApi = ["ICoreApi"],
            Constraints = ["No direct UI dependency"]
        });

        topology.SaveCrossWork(new TopologyCrossWorkDefinition
        {
            Id = "AgenticOs/CrossWork/LiveOps",
            Name = "LiveOps",
            Description = "Cross-module liveops task",
            Participants =
            [
                new TopologyCrossWorkParticipantDefinition
                {
                    ModuleName = "Core",
                    Role = "owner",
                    Contract = "Expose governance-safe extension points",
                    Deliverable = "LiveOps integration"
                },
                new TopologyCrossWorkParticipantDefinition
                {
                    ModuleName = "Foundation",
                    Role = "support",
                    Contract = "Provide shared low-level primitives"
                }
            ]
        });

        topology.BuildTopology();
    }
}
