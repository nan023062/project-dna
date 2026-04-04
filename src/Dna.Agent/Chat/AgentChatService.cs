using System.Text;
using Dna.Agent.Contracts;
using Dna.Agent.Models;
using Dna.Memory.Models;
using Dna.Workbench.Contracts;
using Dna.Workbench.Runtime;

namespace Dna.Agent.Chat;

internal sealed class AgentChatService(
    IAgentProviderCatalog providerCatalog,
    IAgentOrchestrationService orchestrationService,
    IWorkbenchFacade workbench,
    AgentChatSessionStore sessionStore) : IAgentChatService
{
    public Task<AgentProviderState> GetProviderStateAsync(CancellationToken cancellationToken = default)
        => providerCatalog.GetProviderStateAsync(cancellationToken);

    public Task<AgentProviderDescriptor?> SetActiveProviderAsync(
        string providerId,
        CancellationToken cancellationToken = default)
        => providerCatalog.SetActiveProviderAsync(providerId, cancellationToken);

    public Task<AgentChatSessionRecord?> GetSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
        => sessionStore.GetAsync(sessionId, cancellationToken);

    public Task<IReadOnlyList<AgentChatSessionSummary>> ListSessionsAsync(CancellationToken cancellationToken = default)
        => sessionStore.ListAsync(cancellationToken);

    public Task SaveSessionAsync(
        AgentChatSessionRecord session,
        CancellationToken cancellationToken = default)
        => sessionStore.SaveAsync(session, cancellationToken);

    public async Task<AgentChatSendResult> SendAsync(
        AgentChatSendRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);

        var prompt = (request.Prompt ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(prompt))
            throw new InvalidOperationException("prompt is required.");

        var providerState = await providerCatalog.GetProviderStateAsync(cancellationToken);
        var provider = ResolveProvider(providerState, request.ProviderId);
        var existingSession = !string.IsNullOrWhiteSpace(request.SessionId)
            ? await sessionStore.GetAsync(request.SessionId, cancellationToken)
            : null;

        var sessionId = existingSession?.Id;
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            var orchestrationSession = await orchestrationService.StartSessionAsync(new AgentTaskRequest
            {
                Title = BuildSessionTitle(prompt),
                Objective = prompt,
                TargetNodeIds = []
            }, cancellationToken);

            sessionId = orchestrationSession.SessionId;
        }

        var normalizedMode = NormalizeMode(request.Mode);
        PublishRuntimeEvent(sessionId, WorkbenchRuntimeConstants.EventTypes.KnowledgeQueried, "Preparing local agent context.");

        var topology = workbench.Knowledge.GetTopologySnapshot();
        var recall = await workbench.Knowledge.RecallAsync(new RecallQuery
        {
            Question = prompt,
            MaxResults = 3,
            ExpandConstraintChain = true
        }, cancellationToken);

        if (recall.Memories.Count > 0 || recall.ConstraintChain.Count > 0)
        {
            PublishRuntimeEvent(
                sessionId,
                WorkbenchRuntimeConstants.EventTypes.MemoryRead,
                $"Loaded {recall.Memories.Count} memory hits.");
        }

        var assistantMessage = BuildAssistantMessage(prompt, normalizedMode, provider, topology.Modules.Count, recall);
        var session = BuildUpdatedSession(existingSession, sessionId, normalizedMode, prompt, assistantMessage);

        await sessionStore.SaveAsync(session, cancellationToken);
        PublishRuntimeEvent(sessionId, WorkbenchRuntimeConstants.EventTypes.TaskCompleted, "Local agent response completed.");

        return new AgentChatSendResult
        {
            SessionId = sessionId,
            Mode = normalizedMode,
            AssistantMessage = assistantMessage,
            ActiveProviderId = provider?.Id,
            ActiveProviderLabel = provider?.Label ?? "未配置"
        };
    }

    private void PublishRuntimeEvent(string sessionId, string eventType, string message)
    {
        workbench.Runtime.Publish(new WorkbenchRuntimeEvent
        {
            SessionId = sessionId,
            SourceKind = WorkbenchRuntimeConstants.SourceKinds.BuiltInAgent,
            SourceId = "local-chat",
            EventType = eventType,
            Message = message
        });
    }

    private static AgentProviderDescriptor? ResolveProvider(AgentProviderState state, string? requestedProviderId)
    {
        var candidateId = string.IsNullOrWhiteSpace(requestedProviderId)
            ? state.ActiveProviderId
            : requestedProviderId.Trim();

        if (!string.IsNullOrWhiteSpace(candidateId))
        {
            var matched = state.Providers.FirstOrDefault(provider =>
                string.Equals(provider.Id, candidateId, StringComparison.OrdinalIgnoreCase));
            if (matched is not null)
                return matched;
        }

        return state.Providers.FirstOrDefault(provider => provider.Enabled)
               ?? state.Providers.FirstOrDefault();
    }

    private static AgentChatSessionRecord BuildUpdatedSession(
        AgentChatSessionRecord? existingSession,
        string sessionId,
        string mode,
        string prompt,
        string assistantMessage)
    {
        var messages = existingSession?.Messages.ToList() ?? [];
        var createdAtUtc = existingSession?.CreatedAtUtc ?? DateTime.UtcNow;

        messages.Add(new AgentChatMessage
        {
            Role = "user",
            Content = prompt,
            CreatedAtUtc = DateTime.UtcNow
        });

        messages.Add(new AgentChatMessage
        {
            Role = "assistant",
            Content = assistantMessage,
            CreatedAtUtc = DateTime.UtcNow
        });

        return new AgentChatSessionRecord
        {
            Id = sessionId,
            Title = existingSession?.Title ?? BuildSessionTitle(prompt),
            Mode = mode,
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = DateTime.UtcNow,
            Messages = messages
        };
    }

    private static string BuildAssistantMessage(
        string prompt,
        string mode,
        AgentProviderDescriptor? provider,
        int moduleCount,
        RecallResult recall)
    {
        var builder = new StringBuilder();
        var providerLabel = provider?.Label ?? "未配置模型";

        builder.AppendLine($"当前模式：{GetModeLabel(mode)}");
        builder.AppendLine($"当前 Provider：{providerLabel}");
        builder.AppendLine($"已装载模块数：{moduleCount}");

        if (provider is null)
        {
            builder.AppendLine();
            builder.AppendLine("当前还没有可用的 LLM Provider。");
            builder.AppendLine("内置 Chat 已经切到 `Dna.Agent -> Dna.Workbench` 本地链路，但要得到真实模型回复，还需要先在 LLM 配置里启用一个 provider。");
        }
        else
        {
            builder.AppendLine();
            builder.AppendLine("这是当前本地 Agent 骨架基于工作区知识与记忆给出的响应。");
        }

        if (recall.Memories.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("相关记忆：");
            foreach (var memory in recall.Memories.Take(3))
            {
                var summary = string.IsNullOrWhiteSpace(memory.Entry.Summary)
                    ? memory.Entry.Content
                    : memory.Entry.Summary;
                builder.AppendLine($"- {TrimLine(summary, 120)}");
            }
        }

        if (mode == "plan")
        {
            builder.AppendLine();
            builder.AppendLine("建议下一步：");
            builder.AppendLine($"1. 先在知识图谱中定位与“{TrimLine(prompt, 24)}”最相关的模块。");
            builder.AppendLine("2. 对相关模块知识和短期记忆做一次交叉检查。");
            builder.AppendLine("3. 确认变更边界后，再进入实际执行。");
        }
        else if (mode == "agent")
        {
            builder.AppendLine();
            builder.AppendLine("当前内置 Agent 的编排骨架已经接入，但详细工具执行循环还在后续路线里。");
            builder.AppendLine("现阶段它会先完成项目知识查询、记忆召回和运行时状态上报。");
        }
        else
        {
            builder.AppendLine();
            builder.AppendLine($"你的输入是：{TrimLine(prompt, 160)}");
            builder.AppendLine("如果你希望它继续深入，下一步最适合直接补工具调用内核和真实模型执行链。");
        }

        return builder.ToString().TrimEnd();
    }

    private static string TrimLine(string? text, int maxLength)
    {
        var value = (text ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (value.Length <= maxLength)
            return value;

        return value[..maxLength] + "...";
    }

    private static string BuildSessionTitle(string prompt)
    {
        var normalized = TrimLine(prompt, 24);
        return string.IsNullOrWhiteSpace(normalized) ? "新会话" : normalized;
    }

    private static string NormalizeMode(string? mode)
    {
        var value = (mode ?? string.Empty).Trim().ToLowerInvariant();
        return value switch
        {
            "ask" => "ask",
            "plan" => "plan",
            "agent" => "agent",
            "chat" => "chat",
            _ => "agent"
        };
    }

    private static string GetModeLabel(string mode)
    {
        return mode switch
        {
            "ask" => "Ask",
            "plan" => "Plan",
            "agent" => "Agent",
            "chat" => "Chat",
            _ => "Agent"
        };
    }
}
