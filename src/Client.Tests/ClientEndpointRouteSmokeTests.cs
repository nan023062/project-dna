using Dna.Client.Interfaces.Api;
using Dna.Client.Services;
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
        var workspaceConfigPath = Path.Combine(Path.GetTempPath(), "dna-client-route-tests", $"{Guid.NewGuid():N}.json");
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(new ClientRuntimeOptions
        {
            ServerBaseUrl = "http://localhost:5051",
            WorkspaceRoot = Directory.GetCurrentDirectory(),
            WorkspaceConfigPath = workspaceConfigPath
        });
        builder.Services.AddSingleton(new HttpClient());
        builder.Services.AddSingleton<ServerDiscoveryService>();
        builder.Services.AddSingleton<ClientWorkspaceStore>();
        builder.Services.AddSingleton<DnaServerApi>();
        builder.Services.AddSingleton<ClientFolderPickerService>();
        builder.Services.AddClientToolingServices();
        var app = builder.Build();

        app.MapClientStatusEndpoints();
        app.MapClientWorkspaceEndpoints();
        app.MapClientDiscoveryEndpoints();
        app.MapClientProxyEndpoints();
        app.MapClientAgentProxyEndpoints();
        app.MapClientToolingEndpoints();

        var routes = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => new RouteInfo(
                endpoint.RoutePattern.RawText ?? string.Empty,
                endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? []))
            .ToList();

        AssertRoute(routes, "/api/client/status", "GET");
        AssertRoute(routes, "/api/client/workspaces", "GET");
        AssertRoute(routes, "/api/client/workspaces", "POST");
        AssertRoute(routes, "/api/client/workspaces/{id}", "PUT");
        AssertRoute(routes, "/api/client/workspaces/current", "PUT");
        AssertRoute(routes, "/api/client/workspaces/{id}", "DELETE");
        AssertRoute(routes, "/api/client/workspaces/discover", "GET");
        AssertRoute(routes, "/api/client/workspaces/current-server", "PUT");

        AssertRoute(routes, "/api/status", "GET");
        AssertRoute(routes, "/api/topology", "GET");
        AssertRoute(routes, "/api/connection/access", "GET");

        AssertRoute(routes, "/api/memory/stats", "GET");
        AssertRoute(routes, "/api/memory/query", "GET");
        AssertRoute(routes, "/api/memory/{id}", "GET");
        AssertRoute(routes, "/api/memory/{id}", "PUT");
        AssertRoute(routes, "/api/memory/{id}", "DELETE");
        AssertRoute(routes, "/api/memory/remember", "POST");

        AssertRoute(routes, "/agent/chat", "POST");
        AssertRoute(routes, "/agent/sessions", "GET");
        AssertRoute(routes, "/agent/sessions/{id}", "GET");
        AssertRoute(routes, "/agent/sessions/save", "POST");
        AssertRoute(routes, "/agent/providers", "GET");
        AssertRoute(routes, "/agent/providers", "POST");
        AssertRoute(routes, "/agent/providers/active", "POST");
        AssertRoute(routes, "/agent/providers/active", "PUT");
        AssertRoute(routes, "/agent/providers/{id}", "DELETE");
        AssertRoute(routes, "/api/agent/edits/keep", "POST");
        AssertRoute(routes, "/api/agent/edits/undo", "POST");

        AssertRoute(routes, "/api/client/tooling/list", "GET");
        AssertRoute(routes, "/api/client/tooling/install", "POST");
        AssertRoute(routes, "/api/client/tooling/select-folder", "POST");
        AssertRoute(routes, "/api/client/mcp/tools", "GET");
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
