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
                    error = "自助注册已关闭，请联系管理员创建账号。"
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
            if (!principal.IsInRole(ServerRoles.Admin))
                return Results.Json(new { error = "仅管理员可查看用户列表。" }, statusCode: 403);

            var list = users.ListUsers().Select(u => new
            {
                u.Id, u.Username, u.Role, u.CreatedAt
            });
            return Results.Json(new { users = list }, JsonOpts);
        }).RequireAuthorization();

        api.MapPost("/users", (AdminCreateUserRequest req, ClaimsPrincipal principal, UserStore users) =>
        {
            if (!principal.IsInRole(ServerRoles.Admin))
                return Results.Json(new { error = "仅管理员可创建用户。" }, statusCode: 403);

            var requestedRole = NormalizeRequestedRole(req.Role, isAdmin: true);
            var (success, message, user) = users.Register(req.Username, req.Password, requestedRole);
            if (!success)
                return Results.Json(new { error = message }, statusCode: 400);

            return Results.Json(new
            {
                user = new { user!.Id, user.Username, user.Role, user.CreatedAt }
            }, JsonOpts);
        }).RequireAuthorization();

        api.MapPut("/users/{id}/role", (string id, UpdateUserRoleRequest req, ClaimsPrincipal principal, UserStore users) =>
        {
            if (!principal.IsInRole(ServerRoles.Admin))
                return Results.Json(new { error = "仅管理员可修改用户角色。" }, statusCode: 403);

            var currentUserId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            var role = NormalizeRequestedRole(req.Role, isAdmin: true);
            var target = users.GetById(id);
            if (target == null)
                return Results.Json(new { error = $"用户 '{id}' 不存在。" }, statusCode: 404);

            if (string.Equals(currentUserId, id, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(target.Role, ServerRoles.Admin, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(role, ServerRoles.Admin, StringComparison.OrdinalIgnoreCase))
            {
                return Results.Json(new { error = "不能将当前登录的管理员账号降级。" }, statusCode: 409);
            }

            var (success, message, user) = users.UpdateRole(id, role);
            if (!success)
                return Results.Json(new { error = message }, statusCode: 400);

            return Results.Json(new
            {
                user = new { user!.Id, user.Username, user.Role, user.CreatedAt }
            }, JsonOpts);
        }).RequireAuthorization();

        api.MapPut("/users/{id}/password", (string id, ResetUserPasswordRequest req, ClaimsPrincipal principal, UserStore users) =>
        {
            if (!principal.IsInRole(ServerRoles.Admin))
                return Results.Json(new { error = "仅管理员可重置密码。" }, statusCode: 403);

            var (success, message, user) = users.ResetPassword(id, req.Password);
            if (!success)
                return Results.Json(new { error = message }, statusCode: 400);

            return Results.Json(new
            {
                user = new { user!.Id, user.Username, user.Role, user.CreatedAt },
                message
            }, JsonOpts);
        }).RequireAuthorization();

        api.MapDelete("/users/{id}", (string id, ClaimsPrincipal principal, UserStore users) =>
        {
            if (!principal.IsInRole(ServerRoles.Admin))
                return Results.Json(new { error = "仅管理员可删除用户。" }, statusCode: 403);

            var currentUserId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.Equals(currentUserId, id, StringComparison.OrdinalIgnoreCase))
                return Results.Json(new { error = "不能删除当前登录的管理员账号。" }, statusCode: 409);

            var (success, message, user) = users.DeleteUser(id);
            if (!success)
                return Results.Json(new { error = message }, statusCode: 400);

            return Results.Json(new
            {
                user = new { user!.Id, user.Username, user.Role, user.CreatedAt },
                message
            }, JsonOpts);
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
public record AdminCreateUserRequest(string Username, string Password, string? Role);
public record UpdateUserRoleRequest(string Role);
public record ResetUserPasswordRequest(string Password);
