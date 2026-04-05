using Dna.Agent.Contracts;
using Dna.Agent.Models;
using Dna.Core.Config;

namespace Dna.App.Services.Agent;

public sealed class AppAgentProviderCatalog(AppProjectLlmConfigService llmConfigService) : IAgentProviderCatalog
{
    public Task<AgentProviderState> GetProviderStateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var config = llmConfigService.Load();
        var activeProviderId = ResolveActiveProviderId(config);
        var providers = config.Providers
            .Select(provider => new AgentProviderDescriptor
            {
                Id = provider.Id,
                Name = provider.Name,
                ProviderType = provider.ProviderType,
                Model = provider.Model,
                Enabled = provider.Enabled,
                IsActive = string.Equals(provider.Id, activeProviderId, StringComparison.OrdinalIgnoreCase),
                Label = BuildLabel(provider)
            })
            .OrderByDescending(provider => provider.IsActive)
            .ThenBy(provider => provider.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Task.FromResult(new AgentProviderState
        {
            ActiveProviderId = activeProviderId,
            Providers = providers
        });
    }

    public async Task<AgentProviderDescriptor?> SetActiveProviderAsync(
        string providerId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(providerId))
            throw new InvalidOperationException("providerId is required.");

        var config = llmConfigService.Load();
        var provider = config.Providers.FirstOrDefault(item =>
            string.Equals(item.Id, providerId.Trim(), StringComparison.OrdinalIgnoreCase));
        if (provider is null)
            throw new InvalidOperationException($"Provider not found: {providerId}");

        config.ActiveProviderId = provider.Id;
        config.Purposes.Chat = provider.Id;
        llmConfigService.Save(config);

        var state = await GetProviderStateAsync(cancellationToken);
        return state.Providers.FirstOrDefault(item => item.IsActive);
    }

    private static string? ResolveActiveProviderId(RuntimeLlmConfigDocument config)
    {
        var chatProvider = NormalizeOptional(config.Purposes.Chat);
        if (!string.IsNullOrWhiteSpace(chatProvider))
            return chatProvider;

        var activeProvider = NormalizeOptional(config.ActiveProviderId);
        if (!string.IsNullOrWhiteSpace(activeProvider))
            return activeProvider;

        return config.Providers.FirstOrDefault(provider => provider.Enabled)?.Id
               ?? config.Providers.FirstOrDefault()?.Id;
    }

    private static string BuildLabel(RuntimeLlmProviderConfig provider)
    {
        var name = NormalizeOptional(provider.Name) ?? "Unknown";
        var model = NormalizeOptional(provider.Model) ?? "default";
        return $"{name} ({model})";
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
