using Dna.App.Services;

namespace Dna.App.Interfaces.Api;

public static class AppWorkspaceEndpoints
{
    public static void MapAppWorkspaceEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/app/workspaces", (AppWorkspaceStore store) =>
            Results.Ok(store.GetSnapshot()));

        app.MapPost("/api/app/workspaces", (AppWorkspaceUpsertRequest request, AppWorkspaceStore store) =>
            Execute(() =>
            {
                var workspace = store.CreateWorkspace(request);
                return Results.Ok(new
                {
                    workspace,
                    snapshot = store.GetSnapshot()
                });
            }));

        app.MapPut("/api/app/workspaces/{id}", (string id, AppWorkspaceUpsertRequest request, AppWorkspaceStore store) =>
            Execute(() =>
            {
                var workspace = store.UpdateWorkspace(id, request);
                return Results.Ok(new
                {
                    workspace,
                    snapshot = store.GetSnapshot()
                });
            }));

        app.MapPut("/api/app/workspaces/current", (AppWorkspaceSelectionRequest request, AppWorkspaceStore store) =>
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

        app.MapDelete("/api/app/workspaces/{id}", (string id, AppWorkspaceStore store) =>
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

public sealed class AppWorkspaceSelectionRequest
{
    public string? WorkspaceId { get; init; }
}
