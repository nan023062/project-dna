using Dna.App.Services;
using Dna.Core.Config;
using Xunit;

namespace App.Tests;

[Collection(RuntimeLlmConfigTestCollection.Name)]
public sealed class AppProjectLlmConfigServiceTests
{
    [Fact]
    public void Load_ShouldUseUserScopedLlmConfigFile()
    {
        using var scope = CreateLlmScope();
        var service = new AppProjectLlmConfigService();

        var config = service.Load();

        Assert.NotNull(config);
        Assert.True(File.Exists(service.FilePath));
        Assert.Equal(RuntimeLlmConfigPaths.ResolveGlobalFilePath(), service.FilePath);
    }

    [Fact]
    public void Save_ShouldPersistPurposesAndProviderBindings()
    {
        using var scope = CreateLlmScope();
        var service = new AppProjectLlmConfigService();

        var saved = service.Save(new RuntimeLlmConfigDocument
        {
            ActiveProviderId = "app-openai",
            Providers =
            [
                new RuntimeLlmProviderConfig
                {
                    Id = "app-openai",
                    Name = "App OpenAI",
                    ProviderType = "OpenAI",
                    BaseUrl = "https://api.openai.com/v1",
                    Model = "gpt-4.1",
                    ApiKeySource = "env",
                    ApiKeyEnvVar = "OPENAI_API_KEY",
                    Tags = ["chat", "embedding"]
                }
            ],
            Purposes = new RuntimeLlmPurposeBindings
            {
                Chat = "app-openai",
                Embedding = "app-openai"
            }
        });

        Assert.Equal("app-openai", saved.ActiveProviderId);
        Assert.Single(saved.Providers);
        Assert.Equal("openai", saved.Providers[0].ProviderType);
        Assert.Equal("app-openai", saved.Purposes.Chat);
        Assert.Equal("app-openai", saved.Purposes.Embedding);
    }

    private static RuntimeLlmConfigPathOverrideScope CreateLlmScope()
    {
        var filePath = Path.Combine(Path.GetTempPath(), "app-llm-config-tests", Guid.NewGuid().ToString("N"), "llm.json");
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        return new RuntimeLlmConfigPathOverrideScope(filePath);
    }
}
