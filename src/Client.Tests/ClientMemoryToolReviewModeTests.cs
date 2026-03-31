using System.Net;
using System.Text;
using Dna.Client.Interfaces.Mcp;
using Dna.Client.Services;
using Xunit;

namespace Client.Tests;

public sealed class ClientMemoryToolReviewModeTests
{
    [Fact]
    public async Task Remember_ShouldSubmitCreateReviewRequest()
    {
        var recorder = new RequestRecorder(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.EndsWith("/api/review/memory/submissions", request.RequestUri!.AbsoluteUri);
            return CreateJsonResponse("""{"id":"sub-create"}""");
        });

        var tools = CreateTools(recorder);
        var result = await tools.remember("test content", "Semantic", "engineering", nodeType: "Technical");

        Assert.Contains("sub-create", result);
        Assert.Single(recorder.Requests);
        Assert.Contains(@"""operation"":""create""", recorder.Requests[0].Body);
    }

    [Fact]
    public async Task UpdateMemory_ShouldReadFormalMemoryThenSubmitUpdateReviewRequest()
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
                      "nodeId": "Dna.Client",
                      "tags": ["#review"],
                      "importance": 0.5
                    }
                    """);
            }

            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.EndsWith("/api/review/memory/submissions", request.RequestUri!.AbsoluteUri);
            return CreateJsonResponse("""{"id":"sub-update"}""");
        });

        var tools = CreateTools(recorder);
        var result = await tools.update_memory("mem-1", summary: "updated summary");

        Assert.Contains("sub-update", result);
        Assert.Equal(2, recorder.Requests.Count);
        Assert.Contains(@"""operation"":""update""", recorder.Requests[1].Body);
        Assert.Contains(@"""targetId"":""mem-1""", recorder.Requests[1].Body);
    }

    [Fact]
    public async Task DeleteMemory_ShouldSubmitDeleteReviewRequest()
    {
        var recorder = new RequestRecorder(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.EndsWith("/api/review/memory/submissions", request.RequestUri!.AbsoluteUri);
            return CreateJsonResponse("""{"id":"sub-delete"}""");
        });

        var tools = CreateTools(recorder);
        var result = await tools.delete_memory("mem-2");

        Assert.Contains("sub-delete", result);
        Assert.Single(recorder.Requests);
        Assert.Contains(@"""operation"":""delete""", recorder.Requests[0].Body);
        Assert.Contains(@"""targetId"":""mem-2""", recorder.Requests[0].Body);
    }

    private static MemoryTools CreateTools(RequestRecorder recorder)
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "dna-client-memory-tool-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);

        var configPath = Path.Combine(Path.GetTempPath(), "dna-client-memory-tool-tests", $"{Guid.NewGuid():N}.json");
        var workspaceStore = new ClientWorkspaceStore(new ClientRuntimeOptions
        {
            ServerBaseUrl = "http://dna-server",
            WorkspaceRoot = workspaceRoot,
            WorkspaceConfigPath = configPath
        });

        var httpClient = new HttpClient(recorder) { BaseAddress = new Uri("http://dna-server") };
        var api = new DnaServerApi(httpClient, workspaceStore, new ClientRuntimeOptions
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
