using System.Text.Json;

namespace Dna.Client.Services.Pipeline;

public sealed class AgentPipelineRunner(
    DnaServerApi api,
    ClientPipelineStore store)
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
            run.BlockedReason = "执行管线已禁用";
            run.FinishedAt = DateTime.UtcNow;
            store.SaveLatestRun(run);
            return run;
        }

        var state = new SlotRunState();
        foreach (var slotId in config.ExecutionOrder)
        {
            if (!config.Slots.TryGetValue(slotId, out var slot) || !slot.Enabled)
                continue;

            if (string.Equals(slotId, "developer", StringComparison.OrdinalIgnoreCase)
                && config.RequireArchitectBeforeDeveloper
                && !state.ArchitectPassed)
            {
                run.Status = "Blocked";
                run.BlockedReason = "架构师槽位未通过，开发者槽位被阻断（先复盘再开发）";
                break;
            }

            var slotResult = await RunSlotAsync(slot, run, state, cancellationToken);
            run.Slots.Add(slotResult);

            if (string.Equals(slot.Id, "architect", StringComparison.OrdinalIgnoreCase))
            {
                state.ArchitectPassed = !string.Equals(slotResult.Status, "Failed", StringComparison.OrdinalIgnoreCase);
            }

            if (strictGate && slot.BlockingOnFailure &&
                !string.Equals(slotResult.Status, "Success", StringComparison.OrdinalIgnoreCase))
            {
                run.Status = "Blocked";
                run.BlockedReason = $"{slot.DisplayName} 执行失败，触发严格闸门";
                break;
            }
        }

        if (string.Equals(run.Status, "Running", StringComparison.OrdinalIgnoreCase))
            run.Status = run.Slots.Any(s => !string.Equals(s.Status, "Success", StringComparison.OrdinalIgnoreCase))
                ? "Warning"
                : "Success";

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
            if (string.Equals(slot.Id, "architect", StringComparison.OrdinalIgnoreCase))
                await RunArchitectSlotAsync(slot, run, slotResult, cancellationToken);
            else if (string.Equals(slot.Id, "developer", StringComparison.OrdinalIgnoreCase))
                await RunDeveloperSlotAsync(slot, run, slotResult, state, cancellationToken);
            else
                slotResult.Message = $"未知槽位 {slot.Id}，已跳过。";
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
        var validate = await api.GetAsync("/api/governance/validate", cancellationToken);
        var healthy = validate.TryGetProperty("healthy", out var healthyNode) &&
                      healthyNode.ValueKind == JsonValueKind.True;
        var issues = validate.TryGetProperty("totalIssues", out var issueNode) &&
                     issueNode.ValueKind == JsonValueKind.Number &&
                     issueNode.TryGetInt32(out var count)
            ? count
            : 0;

        var contextMap = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in run.Modules)
        {
            var target = Uri.EscapeDataString(module);
            var current = Uri.EscapeDataString(run.Modules.First());
            var active = Uri.EscapeDataString(string.Join(",", run.Modules));
            var context = await api.GetAsync($"/api/graph/context?target={target}&current={current}&activeModules={active}", cancellationToken);
            contextMap[module] = context;
        }

        var recalls = new List<JsonElement>();
        foreach (var question in slot.RecallQuestions)
        {
            var recall = await api.PostAsync("/api/memory/recall", new
            {
                question,
                maxResults = 5
            }, cancellationToken);
            recalls.Add(recall);
        }

        slotResult.Outputs["governanceReport"] = JsonToObject(validate);
        slotResult.Outputs["contexts"] = contextMap.ToDictionary(kv => kv.Key, kv => JsonToObject(kv.Value));
        slotResult.Outputs["recalls"] = recalls.Select(JsonToObject).ToList();

        if (healthy)
        {
            slotResult.Status = "Success";
            slotResult.Message = "架构复盘通过。";
            slotResult.Findings.Add("治理检查健康，允许进入开发阶段。");
        }
        else
        {
            slotResult.Status = run.StrictGate ? "Failed" : "Warning";
            slotResult.Message = $"治理检查发现 {issues} 个问题。";
            slotResult.Findings.Add($"架构风险：{issues} 个问题。");
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

        var beginTask = await api.PostAsync("/api/graph/begin-task", new
        {
            moduleNames = run.Modules
        }, cancellationToken);

        var recalls = new List<JsonElement>();
        foreach (var question in slot.RecallQuestions)
        {
            var recall = await api.PostAsync("/api/memory/recall", new
            {
                question,
                maxResults = 5
            }, cancellationToken);
            recalls.Add(recall);
        }

        slotResult.Outputs["taskContext"] = JsonToObject(beginTask);
        slotResult.Outputs["recalls"] = recalls.Select(JsonToObject).ToList();
        slotResult.Status = "Success";
        slotResult.Message = "开发上下文已准备完成。";
        slotResult.Findings.Add("已基于复盘后上下文完成开发前准备。");
    }

    private async Task PersistRunMemoryAsync(PipelineRunResult run, CancellationToken cancellationToken)
    {
        var summary = $"执行管线完成：{run.Status}（模块：{string.Join(", ", run.Modules)}）";
        var content = BuildRunMemoryContent(run);
        await api.PostAsync("/api/memory/remember", new
        {
            type = 2, // Episodic
            nodeType = 3, // Team
            disciplines = new[] { "engineering" },
            tags = new[] { "#pipeline-run", "#completed-task" },
            summary,
            content,
            importance = run.Status == "Success" ? 0.7 : 0.85
        }, cancellationToken);
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

    private static List<string> ResolveModules(List<string>? requestedModules, ClientExecutionPipelineConfig config)
    {
        var requested = (requestedModules ?? [])
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Select(m => m.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (requested.Count > 0) return requested;

        if (config.Slots.TryGetValue("architect", out var architect) && architect.DefaultModules.Count > 0)
            return architect.DefaultModules.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        return ["Dna.Client"];
    }

    private static object? JsonToObject(JsonElement element)
    {
        return JsonSerializer.Deserialize<object>(element.GetRawText());
    }
}
