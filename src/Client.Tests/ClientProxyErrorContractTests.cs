using System.Net;
using System.Text;
using System.Text.Json;
using Dna.Client.Interfaces.Api;
using Dna.Client.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Client.Tests;

public class ClientProxyErrorContractTests
{
    [Fact]
    public async Task DnaServerApi_ShouldThrowStructuredException_OnHttpFailure()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict)
            {
                Content = new StringContent("""{"error":"review conflict","code":"REVIEW_CONFLICT"}""", Encoding.UTF8, "application/json")
            })))
        {
            BaseAddress = new Uri("http://dna-server")
        };

        var api = new DnaServerApi(httpClient, new ClientRuntimeOptions { ServerBaseUrl = "http://dna-server" });

        var exception = await Assert.ThrowsAsync<DnaServerApiException>(() => api.PostAsync("/api/review/memory/submissions"));

        Assert.Equal(409, exception.StatusCode);
        Assert.Equal("http://dna-server", exception.TargetServer);
        Assert.Contains("review conflict", exception.ResponseBody);
    }

    [Fact]
    public async Task ClientProxyErrorResults_ShouldPreserveUpstreamStatusAndPayload()
    {
        var exception = new DnaServerApiException(
            403,
            "http://dna-server",
            """{"error":"forbidden","code":"AUTH_FORBIDDEN"}""",
            "Forbidden");

        var result = ClientProxyErrorResults.Create(exception, "http://fallback");
        var (statusCode, body) = await ExecuteAsync(result);

        Assert.Equal(403, statusCode);
        Assert.Equal("forbidden", body.GetProperty("error").GetString());
        Assert.Equal("http://dna-server", body.GetProperty("targetServer").GetString());
        Assert.Equal(403, body.GetProperty("upstreamStatusCode").GetInt32());
        Assert.Equal("AUTH_FORBIDDEN", body.GetProperty("upstreamBody").GetProperty("code").GetString());
    }

    [Fact]
    public async Task ClientProxyErrorResults_ShouldDowngradeUnexpectedFailuresTo502()
    {
        var result = ClientProxyErrorResults.Create(new InvalidOperationException("socket closed"), "http://dna-server");
        var (statusCode, body) = await ExecuteAsync(result);

        Assert.Equal(502, statusCode);
        Assert.Equal("socket closed", body.GetProperty("error").GetString());
        Assert.Equal("http://dna-server", body.GetProperty("targetServer").GetString());
    }

    private static async Task<(int StatusCode, JsonElement Body)> ExecuteAsync(IResult result)
    {
        var context = new DefaultHttpContext();
        context.RequestServices = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();
        await using var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        await result.ExecuteAsync(context);

        responseBody.Position = 0;
        using var document = await JsonDocument.ParseAsync(responseBody);
        return (context.Response.StatusCode, document.RootElement.Clone());
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
