using Dna.Client.Services;

namespace Dna.Client.Interfaces.Api;

public static class ClientWorkspaceEndpoints
{
    public static void MapClientWorkspaceEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/client/workspaces", (ClientWorkspaceStore store) =>
            Results.Ok(store.GetSnapshot()));

        app.MapPost("/api/client/workspaces", (ClientWorkspaceUpsertRequest request, ClientWorkspaceStore store) =>
            Execute(() =>
            {
                var workspace = store.CreateWorkspace(request);
                return Results.Ok(new
                {
                    workspace,
                    snapshot = store.GetSnapshot()
                });
            }));

        app.MapPut("/api/client/workspaces/{id}", (string id, ClientWorkspaceUpsertRequest request, ClientWorkspaceStore store) =>
            Execute(() =>
            {
                var workspace = store.UpdateWorkspace(id, request);
                return Results.Ok(new
                {
                    workspace,
                    snapshot = store.GetSnapshot()
                });
            }));

        app.MapPut("/api/client/workspaces/current", (ClientWorkspaceSelectionRequest request, ClientWorkspaceStore store) =>
            Execute(() =>
            {
                if (string.IsNullOrWhiteSpace(request.WorkspaceId))
                    throw new ArgumentException("workspaceId is required.");

                var workspace = store.SetCurrentWorkspace(request.WorkspaceId);
                return Results.Ok(new
                {
                    workspace,
                    snapshot = store.GetSnapshot()
                });
            }));

        app.MapDelete("/api/client/workspaces/{id}", (string id, ClientWorkspaceStore store) =>
            Execute(() =>
            {
                var removed = store.DeleteWorkspace(id);
                return Results.Ok(new
                {
                    removedWorkspace = removed,
                    snapshot = store.GetSnapshot()
                });
            }));
    }

    private static IResult Execute(Func<IResult> action)
    {
        try
        {
            return action();
        }
        catch (KeyNotFoundException ex)
        {
            return Results.NotFound(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}

public sealed class ClientWorkspaceSelectionRequest
{
    public string? WorkspaceId { get; init; }
}
