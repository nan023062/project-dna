using System.Text;
using System.Text.Json;
using Dna.Web.Shared.AgentShell;

namespace Dna.Client.Services;

public sealed class ClientAgentShellContext(
    DnaServerApi api,
    ClientWorkspaceStore workspaceStore,
    ClientRuntimeOptions options) : IAgentShellContext
{
    public string HostKind => "client";

    public async Task<string> GenerateReplyAsync(AgentChatRequest request, CancellationToken cancellationToken = default)
    {
        var workspace = workspaceStore.GetCurrentWorkspace();
        var latestUserMessage = request.Messages
            .LastOrDefault(item => string.Equals(item.Role, "user", StringComparison.OrdinalIgnoreCase))
            ?.Content
            ?.Trim();

        JsonElement? runtimeStatus = null;
        JsonElement? memoryStats = null;
        string? runtimeError = null;

        try
        {
            runtimeStatus = await api.GetAsync("/api/status", cancellationToken);
        }
        catch (Exception ex)
        {
            runtimeError = ex.Message;
        }

        try
        {
            memoryStats = await api.GetAsync("/api/memory/stats", cancellationToken);
        }
        catch (Exception ex)
        {
            runtimeError ??= ex.Message;
        }

        var sb = new StringBuilder();
        sb.AppendLine("# Client Agent Shell");
        sb.AppendLine();
        sb.AppendLine($"- Mode: {request.Mode}");
        sb.AppendLine($"- Workspace: {workspace.Name}");
        sb.AppendLine($"- Local Runtime: {options.ApiBaseUrl}");
        sb.AppendLine($"- Workspace Root: {workspace.WorkspaceRoot}");
        sb.AppendLine();
        sb.AppendLine("当前版本先收口为单机桌面宿主，聊天只负责本地运行态说明、知识入口提示和操作边界提示。");
        sb.AppendLine("后续会把更完整的 agent 编排能力接到独立共享库里，但不是让 Client 依赖远端 Server。");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(runtimeError))
        {
            sb.AppendLine("## Local Runtime Knowledge");
            sb.AppendLine();
            sb.AppendLine($"本地知识运行时读取失败：{runtimeError}");
        }
        else
        {
            sb.AppendLine("## Local Runtime Knowledge");
            sb.AppendLine();
            if (runtimeStatus is { } status)
            {
                sb.AppendLine($"- Modules: {GetInt(status, "moduleCount", 0)}");
                sb.AppendLine($"- Started At: {GetString(status, "startedAt") ?? "-"}");
                sb.AppendLine($"- Uptime: {GetString(status, "uptime") ?? "-"}");
            }

            if (memoryStats is { } stats)
                sb.AppendLine($"- Memory Total: {GetInt(stats, "total", 0)}");

            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(latestUserMessage))
        {
            sb.AppendLine("## Current Request");
            sb.AppendLine();
            sb.AppendLine(latestUserMessage);
            sb.AppendLine();

            if (ContainsAny(latestUserMessage, "知识", "memory", "修改", "编辑"))
            {
                sb.AppendLine("## Knowledge Boundary");
                sb.AppendLine();
                sb.AppendLine("- 当前分支先按单机管理员版本收口，知识修改直接落到本地正式库。");
                sb.AppendLine("- 后续如果重新引入多人协作，再单独恢复 review 与权限边界。");
                sb.AppendLine();
            }

            if (ContainsAny(latestUserMessage, "编排", "agent", "执行", "workflow", "plan"))
            {
                sb.AppendLine("## Execution Status");
                sb.AppendLine();
                sb.AppendLine("当前只保留轻量聊天壳层与本地知识入口，暂不在这里实现复杂任务编排。");
                sb.AppendLine("后续接入统一 agent 执行库后，Client 会作为独立宿主运行自己的编排任务。");
                sb.AppendLine();
            }
        }

        sb.AppendLine("## Next Action");
        sb.AppendLine();
        sb.AppendLine("如果现在继续推进实现，建议优先把本地知识运行时、MCP 接口和 CLI 入口打稳，再补更复杂的 agent 编排能力。");
        return sb.ToString().Trim();
    }

    private static bool ContainsAny(string text, params string[] keywords)
        => keywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase));

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static int GetInt(JsonElement element, string propertyName, int fallback)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var value))
            return fallback;

        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : fallback;
    }
}
