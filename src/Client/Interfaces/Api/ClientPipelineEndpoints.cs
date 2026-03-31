using Dna.Client.Services.Pipeline;

namespace Dna.Client.Interfaces.Api;

public static class ClientPipelineEndpoints
{
    public static void MapClientPipelineEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/client/pipeline/config", (ClientPipelineStore store) =>
        {
            var config = store.GetConfig();
            return Results.Ok(config);
        });

        app.MapPut("/api/client/pipeline/config", (
            PipelineUpdateRequest request,
            ClientPipelineStore store) =>
        {
            if (request.Config == null)
                return Results.BadRequest(new { error = "config 不能为空" });

            var updated = store.UpdateConfig(request.Config);
            return Results.Ok(updated);
        });

        app.MapGet("/api/client/pipeline/runs/latest", (ClientPipelineStore store) =>
        {
            var latest = store.GetLatestRun();
            return latest == null
                ? Results.NotFound(new { error = "暂无执行记录" })
                : Results.Ok(latest);
        });

        app.MapPost("/api/client/pipeline/run", async (
            PipelineRunRequest request,
            AgentPipelineRunner runner) =>
        {
            var result = await runner.RunAsync(request);
            return Results.Ok(result);
        });
    }
}
