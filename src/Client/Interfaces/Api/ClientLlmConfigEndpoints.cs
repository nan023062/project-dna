using Dna.Client.Services;
using Dna.Core.Config;

namespace Dna.Client.Interfaces.Api;

public static class ClientLlmConfigEndpoints
{
    public static void MapClientLlmConfigEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/client/llm", (ClientProjectLlmConfigService llm) =>
            Results.Ok(new
            {
                filePath = llm.FilePath,
                config = llm.Load(),
                summary = llm.GetSummary()
            }));

        app.MapPut("/api/client/llm", (RuntimeLlmConfigDocument request, ClientProjectLlmConfigService llm) =>
            Results.Ok(new
            {
                success = true,
                filePath = llm.FilePath,
                config = llm.Save(request),
                summary = llm.GetSummary()
            }));
    }
}
