using Dna.Client.Services;
using Dna.Core.Config;
using Xunit;

namespace Client.Tests;

public sealed class ClientProjectLlmConfigServiceTests
{
    [Fact]
    public void Load_ShouldCreateProjectScopedLlmConfigFile()
    {
        var workspaceRoot = CreateWorkspaceRoot();
        Directory.CreateDirectory(Path.Combine(workspaceRoot, ".agentic-os"));

        var service = new ClientProjectLlmConfigService(new ClientRuntimeOptions
        {
            WorkspaceRoot = workspaceRoot,
            ServerBaseUrl = "http://localhost:5051"
        });

        var config = service.Load();

        Assert.Empty(config.Providers);
        Assert.True(File.Exists(service.FilePath));
        Assert.Equal(Path.Combine(workspaceRoot, ".agentic-os", "llm.json"), service.FilePath);
    }

    [Fact]
    public void Save_ShouldPersistPurposesAndProviderBindings()
    {
        var workspaceRoot = CreateWorkspaceRoot();
        Directory.CreateDirectory(Path.Combine(workspaceRoot, ".agentic-os"));

        var service = new ClientProjectLlmConfigService(new ClientRuntimeOptions
        {
            WorkspaceRoot = workspaceRoot,
            ServerBaseUrl = "http://localhost:5051"
        });

        var saved = service.Save(new RuntimeLlmConfigDocument
        {
            ActiveProviderId = "client-openai",
            Providers =
            [
                new RuntimeLlmProviderConfig
                {
                    Id = "client-openai",
                    Name = "Client OpenAI",
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
                Chat = "client-openai",
                Embedding = "client-openai"
            }
        });

        Assert.Equal("client-openai", saved.ActiveProviderId);
        Assert.Single(saved.Providers);
        Assert.Equal("openai", saved.Providers[0].ProviderType);
        Assert.Equal("client-openai", saved.Purposes.Chat);
        Assert.Equal("client-openai", saved.Purposes.Embedding);
    }

    private static string CreateWorkspaceRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "dna-client-llm-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
