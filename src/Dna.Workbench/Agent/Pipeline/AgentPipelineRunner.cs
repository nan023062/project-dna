using Dna.Knowledge;
using Dna.Memory.Models;

namespace Dna.Workbench.Agent.Pipeline;

public sealed class AgentPipelineRunner(
    ITopoGraphApplicationService topology,
    IMemoryEngine memory,
    IGovernanceEngine governance,
    AgentPipelineStore store)
{
    public async Task<PipelineRunResult> RunAsync(PipelineRunRequest request, CancellationToken cancellationToken = default)
    {
        var config = store.GetConfig();
        var modules = ResolveModules(request.Modules, config);
        var strictGate = request.StrictGate ?? config.StrictGate;
        var persistRun = request.PersistRunAsMemory ?? config.PersistRunAsMemory;

        var run = new PipelineRunResult
        {
            Task = string.IsNullOrWhiteSpace(request.Task) ? "未命名任务" : request.Task.Trim(),
            Modules = modules,
            DryRun = request.DryRun,
            StrictGate = strictGate,
            StartedAt = DateTime.UtcNow,
            Status = config.Enabled ? "Running" : "Disabled"
        };

        if (!config.Enabled)
        {
            run.BlockedReason = "执行管线已禁用。";
            run.FinishedAt = DateTime.UtcNow;
            store.SaveLatestRun(run);
            return run;
        }

        topology.BuildTopology();

        var state = new SlotRunState();
        foreach (var slotId in config.ExecutionOrder)
        {
            if (!config.Slots.TryGetValue(slotId, out var slot) || !slot.Enabled)
                continue;

            if (string.Equals(slotId, AgentPipelineConstants.SlotIds.Developer, StringComparison.OrdinalIgnoreCase)
                && config.RequireArchitectBeforeDeveloper
                && !state.ArchitectPassed)
            {
                run.Status = "Blocked";
                run.BlockedReason = "Architect 槽位未通过，Developer 槽位被阻断。";
                break;
            }

            var slotResult = await RunSlotAsync(slot, run, state, cancellationToken);
            run.Slots.Add(slotResult);

            if (string.Equals(slot.Id, AgentPipelineConstants.SlotIds.Architect, StringComparison.OrdinalIgnoreCase))
                state.ArchitectPassed = !string.Equals(slotResult.Status, "Failed", StringComparison.OrdinalIgnoreCase);

            if (strictGate &&
                slot.BlockingOnFailure &&
                !string.Equals(slotResult.Status, "Success", StringComparison.OrdinalIgnoreCase))
            {
                run.Status = "Blocked";
                run.BlockedReason = $"{slot.DisplayName} 执行失败，触发严格闸门。";
                break;
            }
        }

        if (string.Equals(run.Status, "Running", StringComparison.OrdinalIgnoreCase))
        {
            run.Status = run.Slots.Any(static slot => !string.Equals(slot.Status, "Success", StringComparison.OrdinalIgnoreCase))
                ? "Warning"
                : "Success";
        }

        run.FinishedAt = DateTime.UtcNow;
        store.SaveLatestRun(run);

        if (!request.DryRun && persistRun)
            await PersistRunMemoryAsync(run, cancellationToken);

        return run;
    }

    private async Task<SlotRunResult> RunSlotAsync(
        AgentSlotConfig slot,
        PipelineRunResult run,
        SlotRunState state,
        CancellationToken cancellationToken)
    {
        var slotResult = new SlotRunResult
        {
            SlotId = slot.Id,
            SlotName = slot.DisplayName,
            StartedAt = DateTime.UtcNow,
            Status = "Success"
        };

        try
        {
            if (string.Equals(slot.Id, AgentPipelineConstants.SlotIds.Architect, StringComparison.OrdinalIgnoreCase))
                await RunArchitectSlotAsync(slot, run, slotResult, cancellationToken);
            else if (string.Equals(slot.Id, AgentPipelineConstants.SlotIds.Developer, StringComparison.OrdinalIgnoreCase))
                await RunDeveloperSlotAsync(slot, run, slotResult, state, cancellationToken);
            else
                slotResult.Message = $"Unknown slot '{slot.Id}', skipped.";
        }
        catch (Exception ex)
        {
            slotResult.Status = "Failed";
            slotResult.Message = ex.Message;
            slotResult.Findings.Add($"执行异常：{ex.Message}");
        }
        finally
        {
            slotResult.FinishedAt = DateTime.UtcNow;
        }

        return slotResult;
    }

    private async Task RunArchitectSlotAsync(
        AgentSlotConfig slot,
        PipelineRunResult run,
        SlotRunResult slotResult,
        CancellationToken cancellationToken)
    {
        var report = governance.ValidateArchitecture();
        var contexts = new Dictionary<string, ModuleContext>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in run.Modules)
            contexts[module] = topology.GetModuleContext(module, run.Modules.FirstOrDefault(), run.Modules);

        var recalls = new List<RecallResult>();
        foreach (var question in slot.RecallQuestions)
        {
            recalls.Add(await memory.RecallAsync(new RecallQuery
            {
                Question = question,
                MaxResults = 5
            }));
        }

        slotResult.Outputs["governanceReport"] = report;
        slotResult.Outputs["contexts"] = contexts;
        slotResult.Outputs["recalls"] = recalls;

        if (report.IsHealthy)
        {
            slotResult.Status = "Success";
            slotResult.Message = "架构复盘通过。";
            slotResult.Findings.Add("治理检查健康，允许进入开发阶段。");
        }
        else
        {
            slotResult.Status = run.StrictGate ? "Failed" : "Warning";
            slotResult.Message = $"治理检查发现 {report.TotalIssues} 个问题。";
            slotResult.Findings.Add($"架构风险：{report.TotalIssues} 个问题。");
        }
    }

    private async Task RunDeveloperSlotAsync(
        AgentSlotConfig slot,
        PipelineRunResult run,
        SlotRunResult slotResult,
        SlotRunState state,
        CancellationToken cancellationToken)
    {
        _ = state;
        cancellationToken.ThrowIfCancellationRequested();

        var plan = topology.GetExecutionPlan(run.Modules);
        var contexts = run.Modules.ToDictionary(
            module => module,
            module => topology.GetModuleContext(module, run.Modules.FirstOrDefault(), run.Modules),
            StringComparer.OrdinalIgnoreCase);

        var recalls = new List<RecallResult>();
        foreach (var question in slot.RecallQuestions)
        {
            recalls.Add(await memory.RecallAsync(new RecallQuery
            {
                Question = question,
                MaxResults = 5
            }));
        }

        slotResult.Outputs["executionPlan"] = plan;
        slotResult.Outputs["contexts"] = contexts;
        slotResult.Outputs["recalls"] = recalls;
        slotResult.Status = "Success";
        slotResult.Message = "开发上下文已准备完成。";
        slotResult.Findings.Add("已基于复盘结果完成开发前准备。");
    }

    private async Task PersistRunMemoryAsync(PipelineRunResult run, CancellationToken cancellationToken)
    {
        var summary = $"执行管线完成：{run.Status}（模块：{string.Join(", ", run.Modules)}）";
        var content = BuildRunMemoryContent(run);

        await memory.RememberAsync(new RememberRequest
        {
            Type = MemoryType.Episodic,
            NodeType = Dna.Knowledge.NodeType.Team,
            Disciplines = ["engineering"],
            Tags = ["#pipeline-run", "#completed-task"],
            Summary = summary,
            Content = content,
            Importance = run.Status == "Success" ? 0.7 : 0.85
        });
    }

    private static string BuildRunMemoryContent(PipelineRunResult run)
    {
        var lines = new List<string>
        {
            $"任务：{run.Task}",
            $"状态：{run.Status}",
            $"模块：{string.Join(", ", run.Modules)}",
            $"开始：{run.StartedAt:O}",
            $"结束：{run.FinishedAt:O}"
        };

        if (!string.IsNullOrWhiteSpace(run.BlockedReason))
            lines.Add($"阻断原因：{run.BlockedReason}");

        foreach (var slot in run.Slots)
        {
            lines.Add($"[{slot.SlotName}] {slot.Status} - {slot.Message}");
            foreach (var finding in slot.Findings.Take(5))
                lines.Add($"- {finding}");
        }

        return string.Join('\n', lines);
    }

    private static List<string> ResolveModules(List<string>? requestedModules, AgentExecutionPipelineConfig config)
    {
        var requested = (requestedModules ?? [])
            .Where(static module => !string.IsNullOrWhiteSpace(module))
            .Select(static module => module.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (requested.Count > 0)
            return requested;

        if (config.Slots.TryGetValue(AgentPipelineConstants.SlotIds.Architect, out var architect) &&
            architect.DefaultModules.Count > 0)
        {
            return architect.DefaultModules.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        return ["Dna.App"];
    }
}
