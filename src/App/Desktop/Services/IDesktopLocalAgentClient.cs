namespace Dna.App.Desktop.Services;

public interface IDesktopLocalAgentClient
{
    Task<DesktopLocalChatProviderState> GetChatProviderStateAsync(CancellationToken cancellationToken = default);

    Task<DesktopLocalChatProvider?> SetActiveChatProviderAsync(string providerId, CancellationToken cancellationToken = default);

    Task<DesktopLocalChatSendResult> SendChatAsync(DesktopLocalChatSendRequest request, CancellationToken cancellationToken = default);

    Task<DesktopLocalChatSession?> GetChatSessionAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DesktopLocalChatSessionSummary>> ListChatSessionsAsync(CancellationToken cancellationToken = default);

    Task SaveChatSessionAsync(DesktopLocalChatSession session, CancellationToken cancellationToken = default);
}

public sealed record DesktopLocalChatProviderState(
    string? ActiveProviderId,
    IReadOnlyList<DesktopLocalChatProvider> Providers);

public sealed record DesktopLocalChatProvider(
    string Id,
    string Name,
    string ProviderType,
    string Model,
    bool Enabled,
    bool IsActive,
    string Label);

public sealed record DesktopLocalChatSendRequest(
    string? SessionId,
    string Mode,
    string Prompt,
    string? ProjectRoot,
    string? ProviderId);

public sealed record DesktopLocalChatSendResult(
    string SessionId,
    string Mode,
    string AssistantMessage,
    string? ActiveProviderId,
    string ActiveProviderLabel);

public sealed record DesktopLocalChatMessage(
    string Role,
    string Content,
    DateTime CreatedAtUtc);

public sealed record DesktopLocalChatSession(
    string Id,
    string Title,
    string Mode,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    IReadOnlyList<DesktopLocalChatMessage> Messages);

public sealed record DesktopLocalChatSessionSummary(
    string Id,
    string Title,
    string Mode,
    DateTime UpdatedAtUtc,
    int MessageCount);
