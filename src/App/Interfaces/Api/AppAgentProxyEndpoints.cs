using System.Text.Json;
using Dna.App.Services.AgentShell;
using Microsoft.AspNetCore.Mvc;

namespace Dna.App.Interfaces.Api;

public static class AppAgentProxyEndpoints
{
    public static void MapAppAgentProxyEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/agent/providers", ([FromServices] AgentShellService shell) =>
            Results.Ok(shell.GetProviderState()));

        app.MapPost("/agent/providers", (AgentProviderUpsertRequest request, [FromServices] AgentShellService shell) =>
            Results.Ok(shell.UpsertProvider(request)));

        app.MapMethods("/agent/providers/active", ["POST", "PUT"], (AgentProviderActiveRequest request, [FromServices] AgentShellService shell) =>
            Results.Ok(shell.SetActiveProvider(request)));

        app.MapDelete("/agent/providers/{id}", (string id, [FromServices] AgentShellService shell) =>
        {
            shell.DeleteProvider(id);
            return Results.NoContent();
        });

        app.MapGet("/agent/sessions", ([FromServices] AgentShellService shell) =>
            Results.Ok(shell.ListSessions()));

        app.MapGet("/agent/sessions/{id}", (string id, [FromServices] AgentShellService shell) =>
        {
            var session = shell.GetSession(id);
            return session is null
                ? Results.NotFound(new { error = $"Session '{id}' was not found." })
                : Results.Ok(session);
        });

        app.MapPost("/agent/sessions/save", (AgentSessionSaveRequest request, [FromServices] AgentShellService shell) =>
            Results.Ok(shell.SaveSession(request)));

        app.MapPost("/agent/chat", async (HttpContext context, AgentChatRequest request, [FromServices] AgentShellService shell, CancellationToken cancellationToken) =>
        {
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";

            await foreach (var evt in shell.StreamReplyAsync(request, cancellationToken))
            {
                var json = JsonSerializer.Serialize(evt);
                await context.Response.WriteAsync($"data: {json}\n\n", cancellationToken);
                await context.Response.Body.FlushAsync(cancellationToken);
            }
        });

        app.MapPost("/api/agent/edits/keep", () =>
            Results.Ok(new { success = false, message = "当前轻量 agent 不生成可保留的文件改动。" }));

        app.MapPost("/api/agent/edits/undo", () =>
            Results.Ok(new { success = false, message = "当前轻量 agent 不生成可撤销的文件改动。" }));
    }
}
