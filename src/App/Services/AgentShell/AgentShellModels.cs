using System.Text.Json.Serialization;

namespace Dna.App.Services.AgentShell;

public sealed class AgentShellStorageOptions
{
    public string RootDirectory { get; init; } = "";
}

public sealed class AgentChatRequest
{
    public List<AgentChatMessage> Messages { get; init; } = [];
    public string Mode { get; init; } = "agent";
    public string? SessionId { get; init; }
    public bool Resume { get; init; }
}

public sealed class AgentChatMessage
{
    public string Role { get; init; } = "";
    public string? Content { get; init; }
}

public sealed class AgentChatEvent
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "text";

    [JsonPropertyName("content")]
    public string? Content { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("toMode")]
    public string? ToMode { get; init; }

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
}

public sealed class AgentSessionRecord
{
    public string Id { get; init; } = "";
    public string Mode { get; init; } = "agent";
    public string Title { get; init; } = "";
    public List<AgentChatMessage> Messages { get; init; } = [];
    public DateTime UpdatedAt { get; init; } = DateTime.UtcNow;
}

public sealed class AgentSessionSaveRequest
{
    public string Id { get; init; } = "";
    public string Mode { get; init; } = "agent";
    public string Title { get; init; } = "";
    public List<AgentChatMessage> Messages { get; init; } = [];
}

public sealed class AgentProviderRecord
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string ProviderType { get; init; } = "openai";
    public string ApiKey { get; init; } = "";
    public string ApiKeyHint { get; init; } = "";
    public string BaseUrl { get; init; } = "";
    public string Model { get; init; } = "";
    public string EmbeddingBaseUrl { get; init; } = "";
    public string EmbeddingModel { get; init; } = "";
    public DateTime UpdatedAt { get; init; } = DateTime.UtcNow;
}

public sealed class AgentProviderUpsertRequest
{
    public string? Id { get; init; }
    public string? Name { get; init; }
    public string? ProviderType { get; init; }
    public string? ApiKey { get; init; }
    public string? BaseUrl { get; init; }
    public string? Model { get; init; }
    public string? EmbeddingBaseUrl { get; init; }
    public string? EmbeddingModel { get; init; }
}

public sealed class AgentProviderActiveRequest
{
    public string? Id { get; init; }
    public string? ProviderId { get; init; }
}

internal sealed class AgentShellState
{
    public string? ActiveProviderId { get; set; }
    public List<AgentProviderRecord> Providers { get; set; } = [];
    public List<AgentSessionRecord> Sessions { get; set; } = [];
}
