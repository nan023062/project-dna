using Microsoft.AspNetCore.Authorization;

namespace Dna.Auth;

public static class ServerRoles
{
    public const string Viewer = "viewer";
    public const string Editor = "editor";
    public const string Admin = "admin";
}

public static class ServerPolicies
{
    public const string ViewerOrAbove = "ViewerOrAbove";
    public const string EditorOrAbove = "EditorOrAbove";
    public const string AdminOnly = "AdminOnly";

    public static void Configure(AuthorizationOptions options)
    {
        options.AddPolicy(ViewerOrAbove, policy =>
            policy.RequireRole(ServerRoles.Viewer, ServerRoles.Editor, ServerRoles.Admin));

        options.AddPolicy(EditorOrAbove, policy =>
            policy.RequireRole(ServerRoles.Editor, ServerRoles.Admin));

        options.AddPolicy(AdminOnly, policy =>
            policy.RequireRole(ServerRoles.Admin));
    }
}
