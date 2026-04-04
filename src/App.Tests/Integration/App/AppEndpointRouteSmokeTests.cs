using Dna.App.Interfaces.Api;
using Dna.App.Services;
using Dna.App.Services.Tooling;
using Dna.Core.Config;
using Dna.Knowledge;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace App.Tests;

public class AppEndpointRouteSmokeTests
{
    [Fact]
    public void AppRouteMappings_ShouldRegisterExpectedEndpoints()
    {
        var workspaceConfigPath = Path.Combine(Path.GetTempPath(), "dna-app-route-tests", $"{Guid.NewGuid():N}.json");
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(new AppRuntimeOptions
        {
            ServerBaseUrl = "http://localhost:5051",
            WorkspaceRoot = Directory.GetCurrentDirectory(),
            WorkspaceConfigPath = workspaceConfigPath
        });
        builder.Services.AddHttpClient();
        builder.Services.AddSingleton<ProjectConfig>();
        builder.Services.AddKnowledgeGraph();
        builder.Services.AddSingleton<AppWorkspaceStore>();
        builder.Services.AddSingleton<AppProjectLlmConfigService>();
        builder.Services.AddSingleton<DnaServerApi>();
        builder.Services.AddSingleton<AppFolderPickerService>();
        builder.Services.AddAppToolingServices();
        var app = builder.Build();

        app.MapAppLocalKnowledgeEndpoints();
        app.MapAppStatusEndpoints();
        app.MapAppLlmConfigEndpoints();
        app.MapAppWorkspaceEndpoints();
        app.MapAppDiscoveryEndpoints();
        app.MapAppToolingEndpoints();

        var routes = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => new RouteInfo(
                endpoint.RoutePattern.RawText ?? string.Empty,
                endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? []))
            .ToList();

        AssertRoute(routes, "/api/app/status", "GET");
        AssertRoute(routes, "/api/app/llm", "GET");
        AssertRoute(routes, "/api/app/llm", "PUT");
        AssertRoute(routes, "/api/app/workspaces", "GET");
        AssertRoute(routes, "/api/app/workspaces", "POST");
        AssertRoute(routes, "/api/app/workspaces/{id}", "PUT");
        AssertRoute(routes, "/api/app/workspaces/current", "PUT");
        AssertRoute(routes, "/api/app/workspaces/{id}", "DELETE");
        AssertRoute(routes, "/api/app/workspaces/discover", "GET");
        AssertRoute(routes, "/api/app/workspaces/current-server", "PUT");

        AssertRoute(routes, "/api/status", "GET");
        AssertRoute(routes, "/api/session", "GET");
        AssertRoute(routes, "/api/topology", "GET");
        AssertRoute(routes, "/api/mcdp", "GET");
        AssertRoute(routes, "/api/graph/search", "GET");
        AssertRoute(routes, "/api/graph/context", "GET");
        AssertRoute(routes, "/api/graph/begin-task", "POST");
        AssertRoute(routes, "/api/workbench/tasks/resolve-support", "POST");
        AssertRoute(routes, "/api/workbench/tasks/start", "POST");
        AssertRoute(routes, "/api/workbench/tasks/end", "POST");
        AssertRoute(routes, "/api/workbench/tasks/active", "GET");
        AssertRoute(routes, "/api/workbench/tasks/completed", "GET");
        AssertRoute(routes, "/api/workbench/governance/resolve", "POST");
        AssertRoute(routes, "/api/connection/access", "GET");
        AssertRoute(routes, "/api/workspace/tree", "GET");

        AssertRoute(routes, "/api/memory/stats", "GET");
        AssertRoute(routes, "/api/memory/recall", "POST");
        AssertRoute(routes, "/api/memory/query", "GET");
        AssertRoute(routes, "/api/memory/{id}", "GET");
        AssertRoute(routes, "/api/memory/{id}", "PUT");
        AssertRoute(routes, "/api/memory/{id}", "DELETE");
        AssertRoute(routes, "/api/memory/remember", "POST");

        AssertRoute(routes, "/api/app/tooling/list", "GET");
        AssertRoute(routes, "/api/app/tooling/install", "POST");
        AssertRoute(routes, "/api/app/tooling/select-folder", "POST");
        AssertRoute(routes, "/api/app/mcp/tools", "GET");
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
