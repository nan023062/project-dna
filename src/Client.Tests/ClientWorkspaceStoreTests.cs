using Dna.Client.Services;
using Xunit;

namespace Client.Tests;

public sealed class ClientWorkspaceStoreTests : IDisposable
{
    private readonly string _workspaceRoot = Path.Combine(Path.GetTempPath(), "dna-client-workspaces", Guid.NewGuid().ToString("N"));
    private readonly string _configPath;

    public ClientWorkspaceStoreTests()
    {
        Directory.CreateDirectory(_workspaceRoot);
        _configPath = Path.Combine(_workspaceRoot, "client-workspaces.json");
    }

    [Fact]
    public void Store_ShouldSeedDefaultWorkspace_FromRuntimeOptions()
    {
        var store = CreateStore("http://localhost:5051");

        var snapshot = store.GetSnapshot();

        Assert.Single(snapshot.Workspaces);
        Assert.Equal("default", snapshot.CurrentWorkspaceId);
        Assert.Equal("http://localhost:5051", snapshot.CurrentWorkspace.ServerBaseUrl);
        Assert.Equal(_workspaceRoot, snapshot.CurrentWorkspace.WorkspaceRoot);
        Assert.Equal("personal", snapshot.CurrentWorkspace.Mode);
    }

    [Fact]
    public void Store_ShouldPersistCreatedWorkspace_AndSwitchCurrent()
    {
        var store = CreateStore("http://localhost:5051");

        var created = store.CreateWorkspace(new ClientWorkspaceUpsertRequest
        {
            Name = "Remote Team",
            Mode = "team",
            ServerBaseUrl = "https://dna.example.com",
            WorkspaceRoot = _workspaceRoot,
            SetCurrent = true
        });

        Assert.Equal("Remote Team", created.Name);

        var reloaded = CreateStore("http://localhost:5051");
        var snapshot = reloaded.GetSnapshot();

        Assert.Equal(created.Id, snapshot.CurrentWorkspaceId);
        Assert.Contains(snapshot.Workspaces, item => item.Id == created.Id && item.ServerBaseUrl == "https://dna.example.com");
    }

    [Fact]
    public void Store_ShouldResyncDefaultWorkspace_FromLatestRuntimeOptions()
    {
        var initial = CreateStore("http://localhost:5051");
        var firstSnapshot = initial.GetSnapshot();

        Assert.Equal("http://localhost:5051", firstSnapshot.CurrentWorkspace.ServerBaseUrl);

        var reloaded = CreateStore("http://127.0.0.1:5081");
        var secondSnapshot = reloaded.GetSnapshot();

        Assert.Equal("default", secondSnapshot.CurrentWorkspaceId);
        Assert.Equal("http://127.0.0.1:5081", secondSnapshot.CurrentWorkspace.ServerBaseUrl);
        Assert.Equal(_workspaceRoot, secondSnapshot.CurrentWorkspace.WorkspaceRoot);
    }

    [Fact]
    public void Store_ShouldKeepExplicitCurrentWorkspace_WhenResyncingDefault()
    {
        var store = CreateStore("http://localhost:5051");
        var remote = store.CreateWorkspace(new ClientWorkspaceUpsertRequest
        {
            Name = "Remote Team",
            Mode = "team",
            ServerBaseUrl = "https://dna.example.com",
            WorkspaceRoot = _workspaceRoot,
            SetCurrent = true
        });

        var reloaded = CreateStore("http://127.0.0.1:5081");
        var snapshot = reloaded.GetSnapshot();

        Assert.Equal(remote.Id, snapshot.CurrentWorkspaceId);
        Assert.Equal("https://dna.example.com", snapshot.CurrentWorkspace.ServerBaseUrl);
        Assert.Contains(snapshot.Workspaces, item => item.Id == "default" && item.ServerBaseUrl == "http://127.0.0.1:5081");
    }

    [Fact]
    public async Task DnaServerApi_ShouldFollowCurrentWorkspaceServer()
    {
        var store = CreateStore("http://localhost:5051");
        var remote = store.CreateWorkspace(new ClientWorkspaceUpsertRequest
        {
            Name = "Remote Team",
            Mode = "team",
            ServerBaseUrl = "https://dna.example.com",
            WorkspaceRoot = _workspaceRoot,
            SetCurrent = true
        });

        Uri? requestedUri = null;
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requestedUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""{"ok":true}""")
            });
        }));
        var api = new DnaServerApi(httpClient, store, new ClientRuntimeOptions
        {
            ServerBaseUrl = "http://localhost:5051",
            WorkspaceRoot = _workspaceRoot,
            WorkspaceConfigPath = _configPath
        });

        await api.GetAsync("/api/status");

        Assert.NotNull(requestedUri);
        Assert.StartsWith(remote.ServerBaseUrl, requestedUri!.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase);
    }

    private ClientWorkspaceStore CreateStore(string serverBaseUrl)
    {
        return new ClientWorkspaceStore(new ClientRuntimeOptions
        {
            ServerBaseUrl = serverBaseUrl,
            WorkspaceRoot = _workspaceRoot,
            WorkspaceConfigPath = _configPath
        });
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspaceRoot))
            Directory.Delete(_workspaceRoot, recursive: true);
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> sendAsync) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            return sendAsync(request);
        }
    }
}
