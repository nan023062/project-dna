using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dna.Auth;

namespace Dna.Interfaces.Api;

public static class AuthEndpoints
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/auth");

        api.MapPost("/register", (RegisterRequest req, ClaimsPrincipal principal, UserStore users, JwtService jwt) =>
        {
            var isAdmin = principal.IsInRole(ServerRoles.Admin);
            if (!isAdmin && !AllowsSelfRegistration())
            {
                return Results.Json(new
                {
                    error = "Self-registration is disabled. Ask an admin to create your account."
                }, statusCode: 403);
            }

            var requestedRole = NormalizeRequestedRole(req.Role, isAdmin);
            var (success, message, user) = users.Register(req.Username, req.Password, requestedRole);
            if (!success)
                return Results.Json(new { error = message }, statusCode: 400);

            var token = jwt.GenerateToken(user!.Id, user.Username, user.Role);
            return Results.Json(new
            {
                token,
                user = new { user.Id, user.Username, user.Role, user.CreatedAt }
            }, JsonOpts);
        }).AllowAnonymous();

        api.MapPost("/login", (LoginRequest req, UserStore users, JwtService jwt) =>
        {
            var (success, message, user) = users.Authenticate(req.Username, req.Password);
            if (!success)
                return Results.Json(new { error = message }, statusCode: 401);

            var token = jwt.GenerateToken(user!.Id, user.Username, user.Role);
            return Results.Json(new
            {
                token,
                user = new { user.Id, user.Username, user.Role, user.CreatedAt }
            }, JsonOpts);
        }).AllowAnonymous();

        api.MapGet("/me", (ClaimsPrincipal principal, UserStore users) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Results.Json(new { error = "未认证" }, statusCode: 401);

            var user = users.GetById(userId);
            if (user == null)
                return Results.Json(new { error = "用户不存在" }, statusCode: 404);

            return Results.Json(new
            {
                user.Id,
                user.Username,
                user.Role,
                user.CreatedAt
            }, JsonOpts);
        }).RequireAuthorization();

        api.MapGet("/users", (ClaimsPrincipal principal, UserStore users) =>
        {
            if (!principal.IsInRole("admin"))
                return Results.Json(new { error = "仅管理员可查看用户列表" }, statusCode: 403);

            var list = users.ListUsers().Select(u => new
            {
                u.Id, u.Username, u.Role, u.CreatedAt
            });
            return Results.Json(new { users = list }, JsonOpts);
        }).RequireAuthorization();
    }

    private static bool AllowsSelfRegistration()
    {
        var raw = Environment.GetEnvironmentVariable("DNA_ALLOW_SELF_REGISTER");
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        return raw.Equals("1", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("true", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeRequestedRole(string? requestedRole, bool isAdmin)
    {
        var normalized = string.IsNullOrWhiteSpace(requestedRole)
            ? ServerRoles.Editor
            : requestedRole.Trim().ToLowerInvariant();

        if (isAdmin)
            return normalized;

        return normalized == ServerRoles.Viewer
            ? ServerRoles.Viewer
            : ServerRoles.Editor;
    }
}

public record RegisterRequest(string Username, string Password, string? Role);
public record LoginRequest(string Username, string Password);
