using System.Text.Json;
using System.Security.Claims;
using Dna.Auth;

namespace Dna.Services;

public sealed class ServerAccessControlMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ServerAllowlistStore allowlistStore)
    {
        if (!ShouldEnforceAllowlist(context.Request.Path))
        {
            await next(context);
            return;
        }

        var access = allowlistStore.Check(context.Connection.RemoteIpAddress);
        if (access.Allowed)
        {
            context.User = BuildAllowlistPrincipal(access);
            await next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            error = "当前来源 IP 不在服务器白名单中，连接已拒绝。",
            remoteIp = access.RemoteIp,
            reason = access.Reason
        }));
    }

    private static bool ShouldEnforceAllowlist(PathString path)
    {
        var value = path.Value ?? string.Empty;
        if (value.StartsWith("/api/status", StringComparison.OrdinalIgnoreCase))
            return false;
        if (value.StartsWith("/api/connection/access", StringComparison.OrdinalIgnoreCase))
            return false;

        return value.StartsWith("/api", StringComparison.OrdinalIgnoreCase)
               || value.StartsWith("/mcp", StringComparison.OrdinalIgnoreCase);
    }

    private static ClaimsPrincipal BuildAllowlistPrincipal(AccessCheckResult access)
    {
        var role = NormalizeRole(access.Role);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, access.RemoteIp),
            new(ClaimTypes.Name, access.EntryName ?? access.RemoteIp),
            new(ClaimTypes.Role, role)
        };

        var identity = new ClaimsIdentity(claims, authenticationType: "Allowlist");
        return new ClaimsPrincipal(identity);
    }

    private static string NormalizeRole(string? role)
    {
        return role?.Trim().ToLowerInvariant() switch
        {
            ServerRoles.Admin => ServerRoles.Admin,
            ServerRoles.Editor => ServerRoles.Editor,
            ServerRoles.Viewer => ServerRoles.Viewer,
            _ => ServerRoles.Viewer
        };
    }
}
