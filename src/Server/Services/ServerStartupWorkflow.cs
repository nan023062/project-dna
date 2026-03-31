using Dna.Knowledge;
using Dna.Memory.Models;

namespace Dna.Services;

public sealed class ServerStartupWorkflow(
    IGraphEngine graph,
    IMemoryEngine memory,
    ServerRuntimeOptions runtimeOptions,
    ILogger<ServerStartupWorkflow> logger)
{
    private const string DevGroupName = "开发组";

    public Task RunAsync()
    {
        graph.Initialize(runtimeOptions.DataPath);
        memory.Initialize(runtimeOptions.DataPath);

        try
        {
            graph.BuildTopology();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Initial topology build failed. Continuing startup with an empty store.");
        }

        LogDevGroupRetrospective();
        return Task.CompletedTask;
    }

    private void LogDevGroupRetrospective()
    {
        try
        {
            var devGroup = graph.FindModule(DevGroupName);
            if (devGroup == null)
            {
                logger.LogWarning(
                    "Startup workflow could not find module '{DevGroup}'. Skipping activation and retrospective.",
                    DevGroupName);
                return;
            }

            var context = graph.GetModuleContext(DevGroupName, DevGroupName, [DevGroupName]);
            logger.LogInformation(
                "Startup workflow activated module '{Module}' with {ConstraintCount} constraints.",
                DevGroupName,
                context.Constraints?.Count ?? 0);

            var lastCompleted = memory.QueryMemories(new MemoryFilter
            {
                NodeId = DevGroupName,
                Tags = ["#completed-task"],
                Freshness = FreshnessFilter.All,
                Limit = 1
            }).FirstOrDefault();

            var lastLesson = memory.QueryMemories(new MemoryFilter
            {
                NodeId = DevGroupName,
                Tags = ["#lesson"],
                Freshness = FreshnessFilter.All,
                Limit = 1
            }).FirstOrDefault();

            if (lastCompleted == null && lastLesson == null)
            {
                logger.LogInformation(
                    "Startup retrospective found no completed-task or lesson memories for module '{Module}'.",
                    DevGroupName);
                return;
            }

            if (lastCompleted != null)
            {
                logger.LogInformation(
                    "Startup retrospective last completed task: [{Id}] {Summary} @ {CreatedAt}",
                    lastCompleted.Id,
                    lastCompleted.Summary ?? "(no summary)",
                    lastCompleted.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
            }

            if (lastLesson != null)
            {
                logger.LogInformation(
                    "Startup retrospective last lesson: [{Id}] {Summary} @ {CreatedAt}",
                    lastLesson.Id,
                    lastLesson.Summary ?? "(no summary)",
                    lastLesson.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Startup workflow failed during dev-group activation or retrospective.");
        }
    }
}
