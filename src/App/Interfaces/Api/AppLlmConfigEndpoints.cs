using Dna.App.Services;
using Dna.Core.Config;

namespace Dna.App.Interfaces.Api;

public static class AppLlmConfigEndpoints
{
    public static void MapAppLlmConfigEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/app/llm", (AppProjectLlmConfigService llm) =>
            Results.Ok(new
            {
                filePath = llm.FilePath,
                config = llm.Load(),
                summary = llm.GetSummary()
            }));

        app.MapPut("/api/app/llm", (RuntimeLlmConfigDocument request, AppProjectLlmConfigService llm) =>
            Results.Ok(new
            {
                success = true,
                filePath = llm.FilePath,
                config = llm.Save(request),
                summary = llm.GetSummary()
            }));
    }
}
