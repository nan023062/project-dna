using Dna.App.Services;

namespace Dna.App.Interfaces.Api;

public static class AppDiscoveryEndpoints
{
    public static void MapAppDiscoveryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/app/workspaces/discover", (AppRuntimeOptions options, AppWorkspaceStore store) =>
        {
            var snapshot = store.GetSnapshot();
            return Results.Ok(new
            {
                discovered = new[]
                {
                    new
                    {
                        baseUrl = options.ApiBaseUrl,
                        displayName = "local-runtime",
                        allowed = true,
                        role = "admin",
                        accessReason = string.Empty,
                        transport = "local-process"
                    }
                },
                snapshot
            });
        });

        app.MapPut("/api/app/workspaces/current-server", (SetCurrentServerRequest request, AppRuntimeOptions options, AppWorkspaceStore store) =>
            Execute(() =>
            {
                var workspace = store.SetCurrentServer(options.ApiBaseUrl, "local-runtime");
                var snapshot = store.GetSnapshot();
                return Results.Ok(new
                {
                    workspace,
                    snapshot,
                    selected = new
                    {
                        baseUrl = options.ApiBaseUrl,
                        displayName = "local-runtime",
                        allowed = true,
                        role = "admin",
                        accessReason = string.Empty,
                        transport = "local-process"
                    }
                });
            }));
    }

    private static IResult Execute(Func<IResult> action)
    {
        try
        {
            return action();
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

public sealed class SetCurrentServerRequest
{
    public string? ServerBaseUrl { get; init; }
}
