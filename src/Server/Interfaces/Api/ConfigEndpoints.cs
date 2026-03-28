using System.Text.Json;
using System.Text.Json.Serialization;
using Dna.Core.Logging;
using Microsoft.AspNetCore.Mvc;
using Dna.Core.Config;

namespace Dna.Interfaces.Api;

public static class ConfigEndpoints
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static void MapConfigEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/config", (ProjectConfig config) =>
        {
            return Results.Json(new
            {
                projectRoot = config.DefaultProjectRoot,
                configured = config.HasProject,
                recentProjects = config.GetRecentProjects().Select(p => new
                {
                    p.Path,
                    p.Name,
                    p.LastOpened
                })
            }, JsonOpts);
        });

        api.MapPost("/config/project", (
            SetProjectRequest req,
            ProjectConfig config,
            FileLogWriter fileWriter) =>
        {
            var result = config.SetProject(req.ProjectRoot, req.StorePath);
            if (result.Success)
            {
                var store = config.ResolveStore(req.StorePath, config.DefaultProjectRoot);
                fileWriter.SetLogDirectory(store);
            }
            return Results.Json(new
            {
                success = result.Success,
                message = result.Message,
                projectRoot = config.DefaultProjectRoot,
                storePath = config.DnaStorePath
            }, JsonOpts);
        });

        api.MapGet("/browse", ([FromQuery] string? path) =>
        {
            if (path == "__drives__")
            {
                var drives = DriveInfo.GetDrives()
                    .Where(d => d.IsReady)
                    .Select(d => new
                    {
                        name = d.Name.TrimEnd('\\'),
                        path = d.RootDirectory.FullName,
                        isDir = true,
                        isDrive = true
                    })
                    .ToList<object>();

                return Results.Json(new
                {
                    current = "我的电脑",
                    parent = (string?)null,
                    entries = drives,
                    atDriveList = true
                }, JsonOpts);
            }

            var dir = string.IsNullOrEmpty(path)
                ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                : path;

            if (!Directory.Exists(dir))
                return Results.Json(new { error = $"目录不存在: {dir}" }, statusCode: 400);

            var parent = Path.GetDirectoryName(dir);
            var atDriveRoot = parent == null;
            var entries = new List<object>();

            try
            {
                foreach (var d in Directory.EnumerateDirectories(dir))
                {
                    var name = Path.GetFileName(d);
                    if (name.StartsWith('.') || name == "node_modules" || name == "$RECYCLE.BIN" || name == "System Volume Information")
                        continue;
                    entries.Add(new { name, path = d, isDir = true });
                }
            }
            catch (UnauthorizedAccessException) { }

            return Results.Json(new
            {
                current = dir,
                parent,
                entries,
                atDriveRoot
            }, JsonOpts);
        });
    }
}

public record SetProjectRequest(string ProjectRoot, string? StorePath = null);
