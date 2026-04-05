namespace Dna.Agent.Models;

public sealed class AgentProviderDescriptor
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string ProviderType { get; init; } = string.Empty;

    public string Model { get; init; } = string.Empty;

    public bool Enabled { get; init; }

    public bool IsActive { get; init; }

    public string Label { get; init; } = string.Empty;
}

public sealed class AgentProviderState
{
    public string? ActiveProviderId { get; init; }

    public List<AgentProviderDescriptor> Providers { get; init; } = [];
}

public sealed class AgentChatSendRequest
{
    public string? SessionId { get; init; }

    public string Mode { get; init; } = "agent";

    public string Prompt { get; init; } = string.Empty;

    public string? ProjectRoot { get; init; }

    public string? ProviderId { get; init; }
}

public sealed class AgentChatSendResult
{
    public string SessionId { get; init; } = string.Empty;

    public string Mode { get; init; } = "agent";

    public string AssistantMessage { get; init; } = string.Empty;

    public string? ActiveProviderId { get; init; }

    public string ActiveProviderLabel { get; init; } = string.Empty;
}

public sealed class AgentChatMessage
{
    public string Role { get; init; } = string.Empty;

    public string Content { get; init; } = string.Empty;

    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
}

public sealed class AgentChatSessionRecord
{
    public string Id { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string Mode { get; init; } = "agent";

    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; init; } = DateTime.UtcNow;

    public List<AgentChatMessage> Messages { get; init; } = [];
}

public sealed class AgentChatSessionSummary
{
    public string Id { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string Mode { get; init; } = "agent";

    public DateTime UpdatedAtUtc { get; init; } = DateTime.UtcNow;

    public int MessageCount { get; init; }
}
