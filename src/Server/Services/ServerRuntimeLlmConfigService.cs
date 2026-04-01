using Dna.Core.Config;

namespace Dna.Services;

public sealed class ServerRuntimeLlmConfigService(ServerRuntimeOptions runtimeOptions)
{
    public string FilePath => Path.Combine(runtimeOptions.DataPath, "llm", "server-llm.json");

    public RuntimeLlmConfigDocument Load()
        => RuntimeLlmConfigStore.LoadOrCreate(FilePath);

    public RuntimeLlmConfigDocument Save(RuntimeLlmConfigDocument document)
        => RuntimeLlmConfigStore.Save(FilePath, document);

    public object GetSummary()
    {
        var config = Load();
        var enabledProviders = config.Providers.Where(provider => provider.Enabled).ToList();

        return new
        {
            filePath = FilePath,
            providerCount = config.Providers.Count,
            enabledProviderCount = enabledProviders.Count,
            activeProviderId = config.ActiveProviderId,
            purposes = new
            {
                chat = config.Purposes.Chat,
                embedding = config.Purposes.Embedding,
                review = config.Purposes.Review
            },
            providers = config.Providers.Select(provider => new
            {
                provider.Id,
                provider.Name,
                provider.ProviderType,
                provider.BaseUrl,
                provider.Model,
                provider.EmbeddingBaseUrl,
                provider.EmbeddingModel,
                provider.ApiKeySource,
                provider.ApiKeyEnvVar,
                apiKeyConfigured = !string.IsNullOrWhiteSpace(provider.ApiKey),
                provider.Enabled,
                provider.Tags
            }).ToList()
        };
    }

    public object GetStatusSummary()
    {
        var config = Load();
        var enabledProviders = config.Providers.Count(provider => provider.Enabled);

        return new
        {
            providerCount = config.Providers.Count,
            enabledProviderCount = enabledProviders,
            hasActiveProvider = !string.IsNullOrWhiteSpace(config.ActiveProviderId),
            purposes = new
            {
                chat = !string.IsNullOrWhiteSpace(config.Purposes.Chat),
                embedding = !string.IsNullOrWhiteSpace(config.Purposes.Embedding),
                review = !string.IsNullOrWhiteSpace(config.Purposes.Review)
            }
        };
    }
}
