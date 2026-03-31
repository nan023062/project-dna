using Dna.Client.Services;

namespace Dna.Client.Interfaces.Api;

public static class ClientDiscoveryEndpoints
{
    public static void MapClientDiscoveryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/client/workspaces/discover", async (ServerDiscoveryService discovery, ClientWorkspaceStore store) =>
        {
            var discovered = await discovery.DiscoverAsync();
            var snapshot = store.GetSnapshot();
            return Results.Ok(new
            {
                discovered,
                snapshot
            });
        });

        app.MapPut("/api/client/workspaces/current-server", async (SetCurrentServerRequest request, ServerDiscoveryService discovery, ClientWorkspaceStore store) =>
            await ExecuteAsync(async () =>
            {
                if (string.IsNullOrWhiteSpace(request.ServerBaseUrl))
                    throw new ArgumentException("serverBaseUrl is required.");

                var target = await discovery.ProbeServerAsync(request.ServerBaseUrl);

                if (!target.Allowed)
                    return Results.Json(new
                    {
                        error = $"服务器可达，但当前客户端 IP 未进入白名单：{target.BaseUrl}",
                        reason = target.AccessReason
                    }, statusCode: 403);

                var workspace = store.SetCurrentServer(target.BaseUrl, target.DisplayName);
                var snapshot = store.GetSnapshot();
                return Results.Ok(new
                {
                    workspace,
                    snapshot,
                    selected = target
                });
            }));
    }

    private static async Task<IResult> ExecuteAsync(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
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
