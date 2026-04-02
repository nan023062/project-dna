using Dna.Core.Config;

namespace Dna.Client.Services;

public sealed class ClientProjectLlmConfigService(ClientRuntimeOptions runtimeOptions)
{
    public string FilePath => string.IsNullOrWhiteSpace(runtimeOptions.MetadataRootPath)
        ? Path.Combine(runtimeOptions.WorkspaceRoot, ".agentic-os", "llm.json")
        : Path.Combine(Path.GetFullPath(runtimeOptions.MetadataRootPath), "llm.json");

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
}
