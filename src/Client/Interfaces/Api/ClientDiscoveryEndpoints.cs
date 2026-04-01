using Dna.Client.Services;

namespace Dna.Client.Interfaces.Api;

public static class ClientDiscoveryEndpoints
{
    public static void MapClientDiscoveryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/client/workspaces/discover", (ClientRuntimeOptions options, ClientWorkspaceStore store) =>
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

        app.MapPut("/api/client/workspaces/current-server", (SetCurrentServerRequest request, ClientRuntimeOptions options, ClientWorkspaceStore store) =>
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
