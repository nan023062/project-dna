using System.Text.Json;

namespace Dna.App.Services.AgentShell;

public sealed class AgentShellService(
    AgentShellStorageOptions storageOptions,
    IAgentShellContext context)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly object _sync = new();
    private readonly string _stateFilePath = InitializeStatePath(storageOptions.RootDirectory);

    public object GetProviderState()
    {
        var state = LoadState();
        EnsureDefaultProvider(state);

        return new
        {
            providers = state.Providers.OrderByDescending(item => item.UpdatedAt).ToList(),
            activeProviderId = state.ActiveProviderId
        };
    }

    public object UpsertProvider(AgentProviderUpsertRequest request)
    {
        lock (_sync)
        {
            var state = LoadStateLocked();
            var id = string.IsNullOrWhiteSpace(request.Id) ? Guid.NewGuid().ToString("N") : request.Id.Trim();
            var existing = state.Providers.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));

            var provider = new AgentProviderRecord
            {
                Id = id,
                Name = string.IsNullOrWhiteSpace(request.Name) ? request.Model?.Trim() ?? id : request.Name.Trim(),
                ProviderType = string.IsNullOrWhiteSpace(request.ProviderType) ? "openai" : request.ProviderType.Trim().ToLowerInvariant(),
                ApiKey = request.ApiKey?.Trim() ?? existing?.ApiKey ?? string.Empty,
                ApiKeyHint = BuildApiKeyHint(request.ApiKey?.Trim() ?? existing?.ApiKey),
                BaseUrl = request.BaseUrl?.Trim() ?? existing?.BaseUrl ?? string.Empty,
                Model = request.Model?.Trim() ?? existing?.Model ?? "dna-lite",
                EmbeddingBaseUrl = request.EmbeddingBaseUrl?.Trim() ?? existing?.EmbeddingBaseUrl ?? string.Empty,
                EmbeddingModel = request.EmbeddingModel?.Trim() ?? existing?.EmbeddingModel ?? string.Empty,
                UpdatedAt = DateTime.UtcNow
            };

            state.Providers.RemoveAll(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
            state.Providers.Add(provider);
            if (string.IsNullOrWhiteSpace(state.ActiveProviderId))
                state.ActiveProviderId = provider.Id;

            SaveStateLocked(state);
            return GetProviderState();
        }
    }

    public object SetActiveProvider(AgentProviderActiveRequest request)
    {
        var id = string.IsNullOrWhiteSpace(request.Id) ? request.ProviderId?.Trim() : request.Id.Trim();
        lock (_sync)
        {
            var state = LoadStateLocked();
            EnsureDefaultProvider(state);
            if (!state.Providers.Any(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Unknown provider id '{id}'.");

            state.ActiveProviderId = id;
            SaveStateLocked(state);
            return GetProviderState();
        }
    }

    public void DeleteProvider(string id)
    {
        lock (_sync)
        {
            var state = LoadStateLocked();
            var removed = state.Providers.RemoveAll(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
            if (removed == 0)
                return;

            if (string.Equals(state.ActiveProviderId, id, StringComparison.OrdinalIgnoreCase))
                state.ActiveProviderId = state.Providers.FirstOrDefault()?.Id;

            SaveStateLocked(state);
        }
    }

    public object ListSessions()
    {
        var state = LoadState();
        return new
        {
            sessions = state.Sessions
                .OrderByDescending(item => item.UpdatedAt)
                .Select(item => new
                {
                    item.Id,
                    item.Mode,
                    item.Title,
                    item.UpdatedAt,
                    messageCount = item.Messages.Count
                })
                .ToList()
        };
    }

    public AgentSessionRecord? GetSession(string id)
    {
        var state = LoadState();
        return state.Sessions.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public AgentSessionRecord SaveSession(AgentSessionSaveRequest request)
    {
        lock (_sync)
        {
            var state = LoadStateLocked();
            var session = new AgentSessionRecord
            {
                Id = request.Id.Trim(),
                Mode = NormalizeMode(request.Mode),
                Title = string.IsNullOrWhiteSpace(request.Title) ? "Untitled Session" : request.Title.Trim(),
                Messages = request.Messages ?? [],
                UpdatedAt = DateTime.UtcNow
            };

            state.Sessions.RemoveAll(item => string.Equals(item.Id, session.Id, StringComparison.OrdinalIgnoreCase));
            state.Sessions.Add(session);
            SaveStateLocked(state);
            return session;
        }
    }

    public async IAsyncEnumerable<AgentChatEvent> StreamReplyAsync(
        AgentChatRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var mode = NormalizeMode(request.Mode);
        var content = await context.GenerateReplyAsync(new AgentChatRequest
        {
            Messages = request.Messages ?? [],
            Mode = mode,
            Resume = request.Resume,
            SessionId = request.SessionId
        }, cancellationToken);

        foreach (var chunk in Chunk(content))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new AgentChatEvent
            {
                Type = "text",
                Content = chunk
            };
        }

        yield return new AgentChatEvent
        {
            Type = "done"
        };
    }

    private static IEnumerable<string> Chunk(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            yield return "当前没有可返回的内容。";
            yield break;
        }

        const int chunkSize = 220;
        for (var index = 0; index < content.Length; index += chunkSize)
        {
            var length = Math.Min(chunkSize, content.Length - index);
            yield return content.Substring(index, length);
        }
    }

    private AgentShellState LoadState()
    {
        lock (_sync)
        {
            return LoadStateLocked();
        }
    }

    private AgentShellState LoadStateLocked()
    {
        if (!File.Exists(_stateFilePath))
        {
            var initial = new AgentShellState();
            EnsureDefaultProvider(initial);
            SaveStateLocked(initial);
            return initial;
        }

        try
        {
            var json = File.ReadAllText(_stateFilePath);
            var state = JsonSerializer.Deserialize<AgentShellState>(json, JsonOptions) ?? new AgentShellState();
            state.Providers ??= [];
            state.Sessions ??= [];
            EnsureDefaultProvider(state);
            return state;
        }
        catch
        {
            var fallback = new AgentShellState();
            EnsureDefaultProvider(fallback);
            SaveStateLocked(fallback);
            return fallback;
        }
    }

    private void SaveStateLocked(AgentShellState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_stateFilePath)!);
        File.WriteAllText(_stateFilePath, JsonSerializer.Serialize(state, JsonOptions));
    }

    private static void EnsureDefaultProvider(AgentShellState state)
    {
        if (state.Providers.Count == 0)
        {
            state.Providers.Add(new AgentProviderRecord
            {
                Id = "dna-lite",
                Name = "Lightweight Shell",
                ProviderType = "openai",
                ApiKeyHint = "Not required",
                BaseUrl = "local",
                Model = "dna-lite",
                UpdatedAt = DateTime.UtcNow
            });
        }

        if (string.IsNullOrWhiteSpace(state.ActiveProviderId) ||
            !state.Providers.Any(item => string.Equals(item.Id, state.ActiveProviderId, StringComparison.OrdinalIgnoreCase)))
        {
            state.ActiveProviderId = state.Providers[0].Id;
        }
    }

    private static string NormalizeMode(string? mode)
    {
        var value = string.IsNullOrWhiteSpace(mode) ? "agent" : mode.Trim().ToLowerInvariant();
        return value is "agent" or "plan" or "ask" or "chat" ? value : "agent";
    }

    private static string BuildApiKeyHint(string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return "Not configured";

        var trimmed = apiKey.Trim();
        if (trimmed.Length <= 6)
            return new string('*', trimmed.Length);

        return $"{trimmed[..3]}***{trimmed[^3..]}";
    }

    private static string InitializeStatePath(string rootDirectory)
    {
        var normalizedRoot = string.IsNullOrWhiteSpace(rootDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dna", "agent-shell")
            : Path.GetFullPath(rootDirectory);

        Directory.CreateDirectory(normalizedRoot);
        return Path.Combine(normalizedRoot, "agent-shell-state.json");
    }
}
