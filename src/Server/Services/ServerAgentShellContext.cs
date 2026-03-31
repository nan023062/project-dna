using System.Text;
using Dna.Knowledge;
using Dna.Review;
using Dna.Web.Shared.AgentShell;

namespace Dna.Services;

internal sealed class ServerAgentShellContext(
    IGraphEngine graph,
    IMemoryEngine memory,
    MemoryReviewStore reviewStore,
    ServerRuntimeOptions runtimeOptions) : IAgentShellContext
{
    public string HostKind => "server";

    public Task<string> GenerateReplyAsync(AgentChatRequest request, CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;

        var topology = graph.GetTopology();
        var stats = memory.GetMemoryStats();
        var pendingReviews = reviewStore.ListSubmissions(ReviewSubmissionStatus.Pending, null, "memory").Count;
        var latestUserMessage = request.Messages.LastOrDefault(item => string.Equals(item.Role, "user", StringComparison.OrdinalIgnoreCase))?.Content?.Trim();

        var sb = new StringBuilder();
        sb.AppendLine("# Server Agent Shell");
        sb.AppendLine();
        sb.AppendLine($"- Mode: {request.Mode}");
        sb.AppendLine($"- Data Path: {runtimeOptions.DataPath}");
        sb.AppendLine($"- Modules: {topology.Nodes.Count}");
        sb.AppendLine($"- Formal Memories: {stats.Total}");
        sb.AppendLine($"- Pending Reviews: {pendingReviews}");
        sb.AppendLine();
        sb.AppendLine("当前版本只提供轻量 agent 壳层，不做详细任务编排。");
        sb.AppendLine("Server chat 的职责先聚焦在管理视角、知识治理入口和审核边界提示。");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(latestUserMessage))
        {
            sb.AppendLine("## Current Request");
            sb.AppendLine();
            sb.AppendLine(latestUserMessage);
            sb.AppendLine();

            if (ContainsAny(latestUserMessage, "审核", "review", "发布", "正式知识"))
            {
                sb.AppendLine("## Governance Boundary");
                sb.AppendLine();
                sb.AppendLine("- Server 管理端可以审核、发布预审知识。");
                sb.AppendLine("- 管理员客户端可以按既定边界直接上传正式知识。");
                sb.AppendLine("- 普通客户端修改正式知识仍然应走预审。");
                sb.AppendLine();
            }

            if (ContainsAny(latestUserMessage, "编排", "agent", "执行", "workflow", "plan"))
            {
                sb.AppendLine("## Execution Status");
                sb.AppendLine();
                sb.AppendLine("这里暂不承载复杂编排执行器，只保留后续接入统一 agent 执行库所需的宿主壳层。");
                sb.AppendLine();
            }
        }

        sb.AppendLine("## Next Action");
        sb.AppendLine();
        sb.AppendLine("下一步更适合继续沉淀共享 agent 接口、会话状态和模型配置，而不是在 Server 端提前铺复杂执行逻辑。");
        return Task.FromResult(sb.ToString().Trim());
    }

    private static bool ContainsAny(string text, params string[] keywords)
    {
        return keywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }
}
