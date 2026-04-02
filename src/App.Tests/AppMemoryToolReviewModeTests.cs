using System.Net;
using System.Text;
using Dna.App.Interfaces.Mcp;
using Dna.App.Services;
using Xunit;

namespace App.Tests;

public sealed class AppMemoryToolDirectWriteTests
{
    [Fact]
    public async Task Remember_ShouldWriteFormalMemoryDirectly()
    {
        var recorder = new RequestRecorder(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.EndsWith("/api/memory/remember", request.RequestUri!.AbsoluteUri);
            return CreateJsonResponse("""{"id":"mem-create"}""");
        });

        var tools = CreateTools(recorder);
        var result = await tools.remember("test content", "Semantic", "engineering", nodeType: "Technical");

        Assert.Contains("mem-create", result);
        Assert.Single(recorder.Requests);
        Assert.DoesNotContain(@"""operation"":""create""", recorder.Requests[0].Body);
    }

    [Fact]
    public async Task UpdateMemory_ShouldReadThenWriteFormalMemoryDirectly()
    {
        var recorder = new RequestRecorder(request =>
        {
            if (request.Method == HttpMethod.Get)
            {
                Assert.EndsWith("/api/memory/mem-1", request.RequestUri!.AbsoluteUri);
                return CreateJsonResponse(
                    """
                    {
                      "id": "mem-1",
                      "content": "formal content",
                      "summary": "formal summary",
                      "type": "Semantic",
                      "nodeType": "Technical",
                      "source": "Ai",
                      "disciplines": ["engineering"],
                      "features": ["review"],
                      "nodeId": "Dna.App",
                      "tags": ["#review"],
                      "importance": 0.5
                    }
                    """);
            }

            Assert.Equal(HttpMethod.Put, request.Method);
            Assert.EndsWith("/api/memory/mem-1", request.RequestUri!.AbsoluteUri);
            return CreateJsonResponse("""{"id":"mem-1","summary":"updated summary"}""");
        });

        var tools = CreateTools(recorder);
        var result = await tools.update_memory("mem-1", summary: "updated summary");

        Assert.Contains("updated", result);
        Assert.Equal(2, recorder.Requests.Count);
        Assert.DoesNotContain(@"""operation"":""update""", recorder.Requests[1].Body);
        Assert.Contains(@"""Summary"":""updated summary""", recorder.Requests[1].Body);
    }

    [Fact]
    public async Task DeleteMemory_ShouldDeleteFormalMemoryDirectly()
    {
        var recorder = new RequestRecorder(request =>
        {
            Assert.Equal(HttpMethod.Delete, request.Method);
            Assert.EndsWith("/api/memory/mem-2", request.RequestUri!.AbsoluteUri);
            return CreateJsonResponse("""{"message":"Deleted [mem-2]"}""");
        });

        var tools = CreateTools(recorder);
        var result = await tools.delete_memory("mem-2");

        Assert.Contains("Deleted [mem-2]", result);
        Assert.Single(recorder.Requests);
        Assert.True(string.IsNullOrWhiteSpace(recorder.Requests[0].Body));
    }

    private static MemoryTools CreateTools(RequestRecorder recorder)
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "dna-app-memory-tool-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);

        var configPath = Path.Combine(Path.GetTempPath(), "dna-app-memory-tool-tests", $"{Guid.NewGuid():N}.json");
        var workspaceStore = new AppWorkspaceStore(new AppRuntimeOptions
        {
            ServerBaseUrl = "http://dna-server",
            WorkspaceRoot = workspaceRoot,
            WorkspaceConfigPath = configPath
        });

        var httpClient = new HttpClient(recorder) { BaseAddress = new Uri("http://dna-server") };
        var api = new DnaServerApi(httpClient, new AppRuntimeOptions
        {
            ServerBaseUrl = "http://dna-server",
            WorkspaceRoot = workspaceRoot,
            WorkspaceConfigPath = configPath
        });

        return new MemoryTools(api);
    }

    private static HttpResponseMessage CreateJsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class RequestRecorder(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<Record> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new Record(request.Method, request.RequestUri!, body));
            return responder(request);
        }
    }

    private sealed record Record(HttpMethod Method, Uri RequestUri, string Body);
}
