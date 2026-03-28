using Dna.Knowledge;
using Dna.Core.Config;

namespace Dna.Services;

/// <summary>
/// 每日定时执行知识压缩（默认凌晨 02:00）。
/// </summary>
public sealed class KnowledgeCondenseScheduler : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<KnowledgeCondenseScheduler> _logger;
    private readonly ProjectConfig _projectConfig;

    public KnowledgeCondenseScheduler(
        IServiceProvider serviceProvider,
        ProjectConfig projectConfig,
        ILogger<KnowledgeCondenseScheduler> logger)
    {
        _serviceProvider = serviceProvider;
        _projectConfig = projectConfig;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("KnowledgeCondenseScheduler 已启动（按配置触发）");

        while (!stoppingToken.IsCancellationRequested)
        {
            var schedule = _projectConfig.GetGovernanceCondenseSchedule();
            if (!schedule.Enabled)
            {
                _logger.LogDebug("知识压缩调度已关闭，5 分钟后重试配置检查");
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
                continue;
            }

            var now = DateTime.Now;
            var next = now.Date.AddHours(schedule.HourLocal);
            if (next <= now) next = next.AddDays(1);
            var delay = next - now;

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }

            await RunOnce(stoppingToken, schedule.MaxSourceMemories);
        }
    }

    private async Task RunOnce(CancellationToken ct, int maxSourceMemories)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var graph = scope.ServiceProvider.GetRequiredService<IGraphEngine>();
            var governance = scope.ServiceProvider.GetRequiredService<IGovernanceEngine>();

            graph.BuildTopology();
            var results = await governance.CondenseAllNodesAsync(maxSourceMemories);
            var condensed = results.Count(r => !string.IsNullOrWhiteSpace(r.NewIdentityMemoryId));
            var archived = results.Sum(r => r.ArchivedCount);

            _logger.LogInformation(
                "定时知识压缩完成: nodes={Nodes}, condensed={Condensed}, archived={Archived}, maxSource={MaxSource}",
                results.Count, condensed, archived, maxSourceMemories);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "定时知识压缩执行失败");
        }
    }
}

