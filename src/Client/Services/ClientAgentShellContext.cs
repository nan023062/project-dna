using System.Text;
using System.Text.Json;
using Dna.Web.Shared.AgentShell;

namespace Dna.Client.Services;

public sealed class ClientAgentShellContext(
    DnaServerApi api,
    ClientWorkspaceStore workspaceStore) : IAgentShellContext
{
    public string HostKind => "client";

    public async Task<string> GenerateReplyAsync(AgentChatRequest request, CancellationToken cancellationToken = default)
    {
        var workspace = workspaceStore.GetCurrentWorkspace();
        var latestUserMessage = request.Messages.LastOrDefault(item => string.Equals(item.Role, "user", StringComparison.OrdinalIgnoreCase))?.Content?.Trim();

        JsonElement? serverStatus = null;
        JsonElement? memoryStats = null;
        string? upstreamError = null;

        try
        {
            serverStatus = await api.GetAsync("/api/status", cancellationToken);
        }
        catch (Exception ex)
        {
            upstreamError = ex.Message;
        }

        try
        {
            memoryStats = await api.GetAsync("/api/memory/stats", cancellationToken);
        }
        catch (Exception ex)
        {
            upstreamError ??= ex.Message;
        }

        var sb = new StringBuilder();
        sb.AppendLine("# Client Agent Shell");
        sb.AppendLine();
        sb.AppendLine($"- Mode: {request.Mode}");
        sb.AppendLine($"- Workspace: {workspace.Name}");
        sb.AppendLine($"- Target Server: {workspace.ServerBaseUrl}");
        sb.AppendLine($"- Workspace Root: {workspace.WorkspaceRoot}");
        sb.AppendLine();
        sb.AppendLine("当前版本只提供轻量 agent 壳层，不做详细任务编排。");
        sb.AppendLine("后续前后端会共用一套独立的 agent 编排与执行能力库，当前聊天更偏向状态理解、知识入口和操作边界提示。");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(upstreamError))
        {
            sb.AppendLine("## Shared Runtime Knowledge");
            sb.AppendLine();
            sb.AppendLine($"无法连接共享知识库 Server：{upstreamError}");
        }
        else
        {
            sb.AppendLine("## Shared Runtime Knowledge");
            sb.AppendLine();
            if (serverStatus is { } status)
            {
                sb.AppendLine($"- Server Modules: {GetInt(status, "moduleCount", 0)}");
                sb.AppendLine($"- Server Started At: {GetString(status, "startedAt") ?? "-"}");
                sb.AppendLine($"- Server Uptime: {GetString(status, "uptime") ?? "-"}");
            }
            if (memoryStats is { } stats)
            {
                sb.AppendLine($"- Formal Memory Total: {GetInt(stats, "total", 0)}");
            }
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(latestUserMessage))
        {
            sb.AppendLine("## Current Request");
            sb.AppendLine();
            sb.AppendLine(latestUserMessage);
            sb.AppendLine();

            if (ContainsAny(latestUserMessage, "知识", "memory", "review", "预审", "修改"))
            {
                sb.AppendLine("## Knowledge Boundary");
                sb.AppendLine();
                sb.AppendLine("- Client 侧知识修改必须提交到 review submission。");
                sb.AppendLine("- Client 不直接改正式知识库。");
                sb.AppendLine("- 正式知识由 Server 侧管理员审核与发布。");
                sb.AppendLine();
            }

            if (ContainsAny(latestUserMessage, "编排", "agent", "执行", "workflow", "plan"))
            {
                sb.AppendLine("## Execution Status");
                sb.AppendLine();
                sb.AppendLine("当前只保留轻量聊天壳层与共享接口边界，暂不在这里实现复杂任务编排。");
                sb.AppendLine("后续接入统一 agent 执行库后，Client 会作为独立宿主运行自己的编排任务。");
                sb.AppendLine();
            }
        }

        sb.AppendLine("## Next Action");
        sb.AppendLine();
        sb.AppendLine("如果你现在要推进实现，我建议继续围绕共享 agent 壳层的数据模型、会话持久化和运行边界做稳定化，而不是提前堆复杂编排逻辑。");
        return sb.ToString().Trim();
    }

    private static bool ContainsAny(string text, params string[] keywords)
    {
        return keywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

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
