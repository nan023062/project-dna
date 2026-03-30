using System.ComponentModel;
using System.Text.Json;
using Dna.Client.Services.Pipeline;
using ModelContextProtocol.Server;

namespace Dna.Client.Interfaces.Mcp;

[McpServerToolType]
public sealed class PipelineTools(
    ClientPipelineStore store,
    AgentPipelineRunner runner)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [McpServerTool, Description("获取客户端 Agent 执行管线配置（架构师/开发者槽位）。")]
    public string get_execution_pipeline_config()
    {
        var config = store.GetConfig();
        return JsonSerializer.Serialize(config, JsonOptions);
    }

    [McpServerTool, Description("更新客户端 Agent 执行管线配置。参数 configJson 为完整 JSON 配置。")]
    public string update_execution_pipeline_config(
        [Description("完整配置 JSON，结构同 get_execution_pipeline_config 返回。")] string configJson)
    {
        try
        {
            var config = JsonSerializer.Deserialize<ClientExecutionPipelineConfig>(configJson, JsonOptions);
            if (config == null) return "错误：配置解析失败。";
            var updated = store.UpdateConfig(config);
            return JsonSerializer.Serialize(updated, JsonOptions);
        }
        catch (Exception ex)
        {
            return $"错误：{ex.Message}";
        }
    }

    [McpServerTool, Description("执行客户端 Agent 管线。默认顺序：架构师先复盘，再开发者执行。")]
    public async Task<string> run_execution_pipeline(
        [Description("任务描述")] string? task = null,
        [Description("模块列表，逗号分隔。留空则使用配置默认模块。")] string? modules = null,
        [Description("是否仅演练，不写记忆")] bool dryRun = false,
        [Description("是否启用严格闸门（架构师失败阻断开发）")] bool strictGate = true)
    {
        try
        {
            var request = new PipelineRunRequest
            {
                Task = task,
                Modules = SplitCsvOrNull(modules),
                DryRun = dryRun,
                StrictGate = strictGate
            };

            var result = await runner.RunAsync(request);
            return JsonSerializer.Serialize(result, JsonOptions);
        }
        catch (Exception ex)
        {
            return $"错误：{ex.Message}";
        }
    }

    [McpServerTool, Description("查看最近一次执行管线结果。")]
    public string get_latest_pipeline_run()
    {
        var latest = store.GetLatestRun();
        return latest == null
            ? "暂无执行记录。"
            : JsonSerializer.Serialize(latest, JsonOptions);
    }

    private static List<string>? SplitCsvOrNull(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return null;
        var values = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .ToList();
        return values.Count == 0 ? null : values;
    }
}
