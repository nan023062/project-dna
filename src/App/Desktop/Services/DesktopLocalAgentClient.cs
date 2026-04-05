using Dna.Agent.Contracts;
using Dna.Agent.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Dna.App.Desktop.Services;

public sealed class DesktopLocalAgentClient(EmbeddedAppHost host) : IDesktopLocalAgentClient
{
    public async Task<DesktopLocalChatProviderState> GetChatProviderStateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var service = GetRequiredService<IAgentChatService>();
        var state = await service.GetProviderStateAsync(cancellationToken);

        return new DesktopLocalChatProviderState(
            state.ActiveProviderId,
            state.Providers.Select(MapProvider).ToList());
    }

    public async Task<DesktopLocalChatProvider?> SetActiveChatProviderAsync(string providerId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var service = GetRequiredService<IAgentChatService>();
        var provider = await service.SetActiveProviderAsync(providerId, cancellationToken);
        return provider is null ? null : MapProvider(provider);
    }

    public async Task<DesktopLocalChatSendResult> SendChatAsync(DesktopLocalChatSendRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var service = GetRequiredService<IAgentChatService>();
        var result = await service.SendAsync(new AgentChatSendRequest
        {
            SessionId = request.SessionId,
            Mode = request.Mode,
            Prompt = request.Prompt,
            ProjectRoot = request.ProjectRoot,
            ProviderId = request.ProviderId
        }, cancellationToken);

        return new DesktopLocalChatSendResult(
            result.SessionId,
            result.Mode,
            result.AssistantMessage,
            result.ActiveProviderId,
            result.ActiveProviderLabel);
    }

    public async Task<DesktopLocalChatSession?> GetChatSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var service = GetRequiredService<IAgentChatService>();
        var session = await service.GetSessionAsync(sessionId, cancellationToken);
        return session is null ? null : MapSession(session);
    }

    public async Task<IReadOnlyList<DesktopLocalChatSessionSummary>> ListChatSessionsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var service = GetRequiredService<IAgentChatService>();
        var sessions = await service.ListSessionsAsync(cancellationToken);
        return sessions
            .Select(item => new DesktopLocalChatSessionSummary(
                item.Id,
                item.Title,
                item.Mode,
                item.UpdatedAtUtc,
                item.MessageCount))
            .ToList();
    }

    public async Task SaveChatSessionAsync(DesktopLocalChatSession session, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(session);

        var service = GetRequiredService<IAgentChatService>();
        await service.SaveSessionAsync(new AgentChatSessionRecord
        {
            Id = session.Id,
            Title = session.Title,
            Mode = session.Mode,
            CreatedAtUtc = session.CreatedAtUtc,
            UpdatedAtUtc = session.UpdatedAtUtc,
            Messages = session.Messages
                .Select(message => new AgentChatMessage
                {
                    Role = message.Role,
                    Content = message.Content,
                    CreatedAtUtc = message.CreatedAtUtc
                })
                .ToList()
        }, cancellationToken);
    }

    private T GetRequiredService<T>() where T : notnull
    {
        var services = host.Services
            ?? throw new InvalidOperationException("Desktop local runtime is not running.");

        return services.GetRequiredService<T>();
    }

    private static DesktopLocalChatProvider MapProvider(AgentProviderDescriptor provider)
        => new(
            provider.Id,
            provider.Name,
            provider.ProviderType,
            provider.Model,
            provider.Enabled,
            provider.IsActive,
            provider.Label);

    private static DesktopLocalChatSession MapSession(AgentChatSessionRecord session)
        => new(
            session.Id,
            session.Title,
            session.Mode,
            session.CreatedAtUtc,
            session.UpdatedAtUtc,
            session.Messages
                .Select(message => new DesktopLocalChatMessage(
                    message.Role,
                    message.Content,
                    message.CreatedAtUtc))
                .ToList());
}
