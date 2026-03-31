using Dna.Client.Interfaces.Api;
using Dna.Client.Services;
using Dna.Client.Services.Pipeline;
using Dna.Client.Services.Tooling;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Client.Tests;

public class ClientEndpointRouteSmokeTests
{
    [Fact]
    public void ClientRouteMappings_ShouldRegisterExpectedEndpoints()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(new ClientRuntimeOptions { ServerBaseUrl = "http://localhost:5051" });
        builder.Services.AddSingleton(new HttpClient());
        builder.Services.AddSingleton<DnaServerApi>();
        builder.Services.AddSingleton<ClientPipelineStore>();
        builder.Services.AddSingleton<AgentPipelineRunner>();
        builder.Services.AddClientToolingServices();
        var app = builder.Build();

        app.MapClientStatusEndpoints();
        app.MapClientProxyEndpoints();
        app.MapClientPipelineEndpoints();
        app.MapClientToolingEndpoints();

        var routes = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => new RouteInfo(
                endpoint.RoutePattern.RawText ?? string.Empty,
                endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? []))
            .ToList();

        AssertRoute(routes, "/api/client/status", "GET");

        AssertRoute(routes, "/api/status", "GET");
        AssertRoute(routes, "/api/topology", "GET");
        AssertRoute(routes, "/api/auth/login", "POST");
        AssertRoute(routes, "/api/auth/register", "POST");
        AssertRoute(routes, "/api/auth/me", "GET");
        AssertRoute(routes, "/api/auth/users", "GET");

        AssertRoute(routes, "/api/memory/stats", "GET");
        AssertRoute(routes, "/api/memory/query", "GET");
        AssertRoute(routes, "/api/memory/{id}", "GET");
        AssertRoute(routes, "/api/memory/{id}", "PUT");
        AssertRoute(routes, "/api/memory/{id}", "DELETE");
        AssertRoute(routes, "/api/memory/remember", "POST");

        AssertRoute(routes, "/api/review/memory/submissions/mine", "GET");
        AssertRoute(routes, "/api/review/memory/submissions/{id}", "GET");
        AssertRoute(routes, "/api/review/memory/submissions/{id}", "PUT");
        AssertRoute(routes, "/api/review/memory/submissions/{id}", "DELETE");
        AssertRoute(routes, "/api/review/memory/submissions", "POST");

        AssertRoute(routes, "/api/client/pipeline/config", "GET");
        AssertRoute(routes, "/api/client/pipeline/config", "PUT");
        AssertRoute(routes, "/api/client/pipeline/runs/latest", "GET");
        AssertRoute(routes, "/api/client/pipeline/run", "POST");
        AssertRoute(routes, "/api/client/tooling/list", "GET");
        AssertRoute(routes, "/api/client/tooling/install", "POST");
    }

    private static void AssertRoute(IReadOnlyCollection<RouteInfo> routes, string pattern, string method)
    {
        var hit = routes.Any(route =>
            string.Equals(route.Pattern, pattern, StringComparison.OrdinalIgnoreCase) &&
            route.HttpMethods.Any(httpMethod => string.Equals(httpMethod, method, StringComparison.OrdinalIgnoreCase)));

        var knownRoutes = string.Join(", ", routes.Select(route =>
            $"[{string.Join("/", route.HttpMethods)}]{route.Pattern}"));
        Assert.True(hit, $"Expected route [{method}] {pattern} was not found. Known routes: {knownRoutes}");
    }

    private sealed record RouteInfo(string Pattern, IReadOnlyList<string> HttpMethods);
}
