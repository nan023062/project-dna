using Dna.Core.Config;
using Dna.Services;
using Xunit;

namespace Client.Tests;

public sealed class ServerRuntimeLlmConfigServiceTests
{
    [Fact]
    public void Load_ShouldCreateStoreScopedServerLlmConfigFile()
    {
        var dataPath = CreateDataPath();
        var service = new ServerRuntimeLlmConfigService(new ServerRuntimeOptions
        {
            DataPath = dataPath
        });

        var config = service.Load();

        Assert.Empty(config.Providers);
        Assert.True(File.Exists(service.FilePath));
        Assert.Equal(Path.Combine(dataPath, "llm", "server-llm.json"), service.FilePath);
    }

    [Fact]
    public void Save_ShouldDropUnknownProviderReferences()
    {
        var dataPath = CreateDataPath();
        var service = new ServerRuntimeLlmConfigService(new ServerRuntimeOptions
        {
            DataPath = dataPath
        });

        var saved = service.Save(new RuntimeLlmConfigDocument
        {
            ActiveProviderId = "missing-provider",
            Providers =
            [
                new RuntimeLlmProviderConfig
                {
                    Id = "server-main",
                    Name = "Server Main",
                    ProviderType = "openai",
                    BaseUrl = "https://api.openai.com/v1",
                    Model = "gpt-4.1",
                    ApiKeySource = "env",
                    ApiKeyEnvVar = "DNA_SERVER_OPENAI_KEY"
                }
            ],
            Purposes = new RuntimeLlmPurposeBindings
            {
                Chat = "server-main",
                Review = "missing-provider"
            }
        });

        Assert.Null(saved.ActiveProviderId);
        Assert.Equal("server-main", saved.Purposes.Chat);
        Assert.Null(saved.Purposes.Review);
        Assert.Single(saved.Providers);
    }

    private static string CreateDataPath()
    {
        var path = Path.Combine(Path.GetTempPath(), "dna-server-llm-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
