using Dna.Services;
using Dna.Auth;

namespace Dna.Interfaces.Api;

public static class ConnectionEndpoints
{
    public static void MapConnectionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/connection");

        group.MapGet("/access", (HttpContext context, ServerAllowlistStore allowlist) =>
        {
            var result = allowlist.Check(context.Connection.RemoteIpAddress);
            return Results.Ok(result);
        }).AllowAnonymous();

        group.MapGet("/whitelist", (ServerAllowlistStore allowlist) =>
            Results.Ok(allowlist.GetSnapshot()))
            .RequireAuthorization(ServerPolicies.ViewerOrAbove);

        group.MapPost("/whitelist", (AllowlistMutationRequest request, ServerAllowlistStore allowlist) =>
            Execute(() =>
            {
                var entry = allowlist.Add(request);
                return Results.Ok(new
                {
                    entry,
                    snapshot = allowlist.GetSnapshot()
                });
            }))
            .RequireAuthorization(ServerPolicies.AdminOnly);

        group.MapPut("/whitelist/{id}", (string id, AllowlistMutationRequest request, ServerAllowlistStore allowlist) =>
            Execute(() =>
            {
                var entry = allowlist.Update(id, request);
                return Results.Ok(new
                {
                    entry,
                    snapshot = allowlist.GetSnapshot()
                });
            }))
            .RequireAuthorization(ServerPolicies.AdminOnly);

        group.MapDelete("/whitelist/{id}", (string id, ServerAllowlistStore allowlist) =>
            Execute(() =>
            {
                var removed = allowlist.Delete(id);
                return Results.Ok(new
                {
                    removed,
                    snapshot = allowlist.GetSnapshot()
                });
            }))
            .RequireAuthorization(ServerPolicies.AdminOnly);
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
