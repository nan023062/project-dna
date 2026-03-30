using System.Security.Claims;
using Dna.Auth;

namespace Dna.Review;

internal static class ReviewAuthorization
{
    private const string AnonymousSubmissionEnv = "DNA_ALLOW_ANONYMOUS_REVIEW_SUBMISSIONS";

    public static bool TryGetAuthenticatedActor(
        ClaimsPrincipal principal,
        UserStore users,
        out ReviewActor actor)
    {
        actor = null!;

        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return false;

        var user = users.GetById(userId);
        if (user == null)
            return false;

        actor = new ReviewActor
        {
            UserId = user.Id,
            Username = user.Username,
            Role = user.Role,
            IsAuthenticated = true,
            Source = "api"
        };
        return true;
    }

    public static bool TryGetSubmissionActor(
        ClaimsPrincipal principal,
        UserStore users,
        out ReviewActor actor)
    {
        if (TryGetAuthenticatedActor(principal, users, out actor))
            return true;

        if (!AllowsAnonymousReviewSubmissions())
            return false;

        actor = new ReviewActor
        {
            UserId = "local-editor",
            Username = "local-editor",
            Role = "editor",
            IsAuthenticated = false,
            Source = "local-dev"
        };
        return true;
    }

    public static bool TryGetAdminActor(
        ClaimsPrincipal principal,
        UserStore users,
        out ReviewActor actor)
    {
        actor = null!;
        if (!TryGetAuthenticatedActor(principal, users, out actor))
            return false;

        return string.Equals(actor.Role, "admin", StringComparison.OrdinalIgnoreCase);
    }

    private static bool AllowsAnonymousReviewSubmissions()
    {
        var raw = Environment.GetEnvironmentVariable(AnonymousSubmissionEnv);
        if (string.IsNullOrWhiteSpace(raw))
            return true;

        return !raw.Equals("0", StringComparison.OrdinalIgnoreCase)
            && !raw.Equals("false", StringComparison.OrdinalIgnoreCase)
            && !raw.Equals("no", StringComparison.OrdinalIgnoreCase);
    }
}
