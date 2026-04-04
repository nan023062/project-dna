using System.Text.Json;
using Dna.Agent.Models;
using Dna.Core.Config;

namespace Dna.Agent.Chat;

internal sealed class AgentChatSessionStore(ProjectConfig projectConfig)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public Task<AgentChatSessionRecord?> GetAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var path = ResolveSessionFilePath(sessionId, createDirectory: false);
        if (path is null || !File.Exists(path))
            return Task.FromResult<AgentChatSessionRecord?>(null);

        try
        {
            var json = File.ReadAllText(path);
            return Task.FromResult(Normalize(JsonSerializer.Deserialize<AgentChatSessionRecord>(json, JsonOptions)));
        }
        catch
        {
            return Task.FromResult<AgentChatSessionRecord?>(null);
        }
    }

    public Task<IReadOnlyList<AgentChatSessionSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var directory = ResolveSessionDirectory(createDirectory: false);
        if (directory is null || !Directory.Exists(directory))
            return Task.FromResult<IReadOnlyList<AgentChatSessionSummary>>([]);

        var sessions = new List<AgentChatSessionSummary>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var json = File.ReadAllText(file);
                var session = Normalize(JsonSerializer.Deserialize<AgentChatSessionRecord>(json, JsonOptions));
                if (session is null)
                    continue;

                sessions.Add(new AgentChatSessionSummary
                {
                    Id = session.Id,
                    Title = session.Title,
                    Mode = session.Mode,
                    UpdatedAtUtc = session.UpdatedAtUtc,
                    MessageCount = session.Messages.Count
                });
            }
            catch
            {
            }
        }

        return Task.FromResult<IReadOnlyList<AgentChatSessionSummary>>(
            sessions.OrderByDescending(item => item.UpdatedAtUtc).ToList());
    }

    public Task SaveAsync(AgentChatSessionRecord session, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(session);

        var normalized = Normalize(session)
                         ?? throw new InvalidOperationException("Chat session cannot be empty.");
        var path = ResolveSessionFilePath(normalized.Id, createDirectory: true)
                   ?? throw new InvalidOperationException("Project session store is not available.");

        var json = JsonSerializer.Serialize(normalized, JsonOptions);
        File.WriteAllText(path, json);
        return Task.CompletedTask;
    }

    private string? ResolveSessionFilePath(string sessionId, bool createDirectory)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return null;

        var directory = ResolveSessionDirectory(createDirectory);
        return directory is null ? null : Path.Combine(directory, $"{sessionId.Trim()}.json");
    }

    private string? ResolveSessionDirectory(bool createDirectory)
    {
        if (!projectConfig.HasProject || string.IsNullOrWhiteSpace(projectConfig.SessionStorePath))
            return null;

        var directory = Path.Combine(projectConfig.SessionStorePath, "chat");
        if (createDirectory)
            Directory.CreateDirectory(directory);

        return directory;
    }

    private static AgentChatSessionRecord? Normalize(AgentChatSessionRecord? session)
    {
        if (session is null || string.IsNullOrWhiteSpace(session.Id))
            return null;

        var messages = session.Messages
            .Where(message => !string.IsNullOrWhiteSpace(message.Role) && !string.IsNullOrWhiteSpace(message.Content))
            .Select(message => new AgentChatMessage
            {
                Role = NormalizeRole(message.Role),
                Content = message.Content.Trim(),
                CreatedAtUtc = message.CreatedAtUtc == default ? DateTime.UtcNow : message.CreatedAtUtc
            })
            .ToList();

        return new AgentChatSessionRecord
        {
            Id = session.Id.Trim(),
            Title = string.IsNullOrWhiteSpace(session.Title) ? "未命名会话" : session.Title.Trim(),
            Mode = NormalizeMode(session.Mode),
            CreatedAtUtc = session.CreatedAtUtc == default ? DateTime.UtcNow : session.CreatedAtUtc,
            UpdatedAtUtc = session.UpdatedAtUtc == default ? DateTime.UtcNow : session.UpdatedAtUtc,
            Messages = messages
        };
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

    private static string NormalizeRole(string? role)
    {
        var value = (role ?? string.Empty).Trim().ToLowerInvariant();
        return value switch
        {
            "user" => "user",
            "assistant" => "assistant",
            "system" => "system",
            _ => "assistant"
        };
    }
}
