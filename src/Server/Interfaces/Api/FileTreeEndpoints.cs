using Dna.Auth;

namespace Dna.Interfaces.Api;

public static class FileTreeEndpoints
{
    public static void MapFileTreeEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/files");
        group.RequireAuthorization(ServerPolicies.AdminOnly);

        group.MapGet("/tree", () =>
            Results.Json(new { error = "文件树扫描不可用：Server 不访问项目源码" }, statusCode: 501));

        group.MapGet("/children", () =>
            Results.Json(new { error = "文件树扫描不可用：Server 不访问项目源码" }, statusCode: 501));
    }
}
