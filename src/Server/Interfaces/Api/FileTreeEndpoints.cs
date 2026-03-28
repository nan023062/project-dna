using Dna.Core.Config;
using Dna.Knowledge;
using Microsoft.AspNetCore.Mvc;

namespace Dna.Interfaces.Api;

public static class FileTreeEndpoints
{
    public static void MapFileTreeEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/files");

        group.MapGet("/tree", (IGraphEngine graph, ProjectConfig config) =>
        {
            if (!config.HasProject || string.IsNullOrWhiteSpace(config.DefaultProjectRoot))
                return Results.BadRequest(new { error = "未配置项目路径" });

            graph.Initialize(config.DefaultProjectRoot, config.ResolveStore(null, config.DefaultProjectRoot));
            var roots = graph.ScanProjectRoots(config.DefaultProjectRoot);
            return Results.Ok(new { roots });
        });

        group.MapGet("/children", (
            [FromQuery] string path,
            IGraphEngine graph,
            ProjectConfig config) =>
        {
            if (!config.HasProject || string.IsNullOrWhiteSpace(config.DefaultProjectRoot))
                return Results.BadRequest(new { error = "未配置项目路径" });

            graph.Initialize(config.DefaultProjectRoot, config.ResolveStore(null, config.DefaultProjectRoot));
            var children = graph.ScanDirectory(config.DefaultProjectRoot, path ?? "");
            return Results.Ok(new { children });
        });
    }
}
