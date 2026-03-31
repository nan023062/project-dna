using Dna.Client.Services;

namespace Dna.Client.Interfaces.Api;

public static class ClientStatusEndpoints
{
    public static void MapClientStatusEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/client/status", async (DnaServerApi api) =>
        {
            try
            {
                var serverStatus = await api.GetAsync("/api/status");
                return Results.Ok(new
                {
                    client = "ok",
                    targetServer = api.BaseUrl,
                    serverStatus
                });
            }
            catch (Exception ex)
            {
                return Results.Ok(new
                {
                    client = "degraded",
                    targetServer = api.BaseUrl,
                    error = ex.Message
                });
            }
        });
    }
}
