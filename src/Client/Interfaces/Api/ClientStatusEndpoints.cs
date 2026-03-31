using Dna.Client.Services;

namespace Dna.Client.Interfaces.Api;

public static class ClientStatusEndpoints
{
    public static void MapClientStatusEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/client/status", async (DnaServerApi api, ClientWorkspaceStore workspaces) =>
        {
            var currentWorkspace = workspaces.GetCurrentWorkspace();

            try
            {
                var serverStatus = await api.GetAsync("/api/status");
                var access = await api.GetAsync("/api/connection/access");
                return Results.Ok(new
                {
                    client = "ok",
                    targetServer = api.BaseUrl,
                    currentWorkspace,
                    serverStatus,
                    access
                });
            }
            catch (Exception ex)
            {
                return Results.Ok(new
                {
                    client = "degraded",
                    targetServer = api.BaseUrl,
                    currentWorkspace,
                    error = ex.Message
                });
            }
        });
    }
}
