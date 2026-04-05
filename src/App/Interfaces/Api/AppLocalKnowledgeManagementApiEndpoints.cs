using System.Text.Json;
using System.Text.Json.Serialization;
using Dna.Workbench.Contracts;
using Dna.Workbench.Governance;
using Dna.Workbench.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace Dna.App.Interfaces.Api;

public static class AppLocalKnowledgeManagementApiEndpoints
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static void MapAppLocalKnowledgeManagementApiEndpoints(this IEndpointRouteBuilder app)
    {
        MapWorkbenchEndpoints(app);
    }

    private static void MapWorkbenchEndpoints(IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/workbench");

        api.MapPost("/tasks/resolve-support", async (
            [FromBody] WorkbenchRequirementRequest request,
            [FromServices] IWorkbenchTaskService tasks,
            CancellationToken cancellationToken) =>
        {
            var result = await tasks.ResolveRequirementSupportAsync(request, cancellationToken);
            return Results.Json(result, JsonOpts);
        });

        api.MapPost("/tasks/start", async (
            [FromBody] WorkbenchTaskRequest request,
            [FromServices] IWorkbenchTaskService tasks,
            CancellationToken cancellationToken) =>
        {
            var result = await tasks.StartTaskAsync(request, cancellationToken);
            return Results.Json(result, JsonOpts);
        });

        api.MapPost("/tasks/end", async (
            [FromBody] WorkbenchTaskResult request,
            [FromServices] IWorkbenchTaskService tasks,
            CancellationToken cancellationToken) =>
        {
            var result = await tasks.EndTaskAsync(request, cancellationToken);
            return Results.Json(result, JsonOpts);
        });

        api.MapGet("/tasks/active", async (
            [FromServices] IWorkbenchTaskService tasks,
            CancellationToken cancellationToken) =>
        {
            var result = await tasks.ListActiveTasksAsync(cancellationToken);
            return Results.Json(result, JsonOpts);
        });

        api.MapGet("/tasks/completed", async (
            [FromQuery] int? limit,
            [FromServices] IWorkbenchTaskService tasks,
            CancellationToken cancellationToken) =>
        {
            var result = await tasks.ListCompletedTasksAsync(limit is > 0 ? limit.Value : 50, cancellationToken);
            return Results.Json(result, JsonOpts);
        });

        api.MapPost("/governance/resolve", async (
            [FromBody] WorkbenchGovernanceRequest request,
            [FromServices] IWorkbenchGovernanceService governance,
            CancellationToken cancellationToken) =>
        {
            var result = await governance.ResolveGovernanceAsync(request, cancellationToken);
            return Results.Json(result, JsonOpts);
        });
    }
}