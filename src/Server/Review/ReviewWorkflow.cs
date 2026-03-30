using Dna.Knowledge;
using Dna.Memory.Models;
using Microsoft.AspNetCore.Http;

namespace Dna.Review;

internal static class ReviewWorkflow
{
    public static (ReviewOperation Operation, string? TargetId, string? TargetSnapshotJson,
        string? ProposedPayloadJson, string? NormalizedPayloadJson, RememberRequest? NormalizedRememberRequest, IResult? ErrorResult)
        ValidateSubmissionRequest(
            MemoryReviewSubmissionRequest request,
            IMemoryEngine memory,
            IGraphEngine graph)
    {
        if (!Enum.TryParse<ReviewOperation>(request.Operation, true, out var operation))
        {
            return (default, null, null, null, null, null,
                Results.BadRequest(new { error = $"Invalid review operation '{request.Operation}'." }));
        }

        var targetId = NormalizeOptionalString(request.TargetId);
        MemoryEntry? target = null;
        if (operation is ReviewOperation.Update or ReviewOperation.Delete)
        {
            if (string.IsNullOrWhiteSpace(targetId))
            {
                return (default, null, null, null, null, null,
                    Results.BadRequest(new { error = $"targetId is required for '{operation}' submissions." }));
            }

            target = memory.GetMemoryById(targetId);
            if (target == null)
            {
                return (default, null, null, null, null, null,
                    Results.NotFound(new { error = $"Formal memory '{targetId}' was not found." }));
            }
        }

        if (operation == ReviewOperation.Delete)
        {
            return (
                operation,
                targetId,
                target == null ? null : ReviewJson.Serialize(target),
                null,
                null,
                null,
                null);
        }

        if (request.Memory == null)
        {
            return (default, null, null, null, null, null,
                Results.BadRequest(new { error = "memory is required for create/update submissions." }));
        }

        var normalized = NormalizeRememberRequest(request.Memory);
        var validationError = ValidateNormalizedRememberRequest(normalized, graph);
        if (validationError is not null)
            return (default, null, null, null, null, null, validationError);

        return (
            operation,
            targetId,
            target == null ? null : ReviewJson.Serialize(target),
            ReviewJson.Serialize(request.Memory),
            ReviewJson.Serialize(normalized),
            normalized,
            null);
    }

    public static IResult? ValidateNormalizedRememberRequest(RememberRequest request, IGraphEngine graph)
    {
        if (string.IsNullOrWhiteSpace(request.Content))
            return Results.BadRequest(new { error = "memory.content cannot be empty." });

        var nodeId = NormalizeOptionalString(request.NodeId);
        if (!string.IsNullOrWhiteSpace(nodeId))
        {
            var exists = graph.GetAllModules().Any(module =>
                string.Equals(module.Id, nodeId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(module.Name, nodeId, StringComparison.OrdinalIgnoreCase));

            if (!exists)
            {
                return Results.BadRequest(new
                {
                    error = $"memory.nodeId '{nodeId}' does not match any known module id or module name."
                });
            }
        }

        return null;
    }

    public static RememberRequest NormalizeRememberRequest(RememberRequest request) => new()
    {
        Type = request.Type,
        NodeType = request.ResolvedNodeType,
        Source = request.Source,
        Content = request.Content.Trim(),
        Summary = NormalizeOptionalString(request.Summary),
        Disciplines = NormalizeStringList(request.Disciplines),
        Features = NormalizeStringListOrNull(request.Features),
        NodeId = NormalizeOptionalString(request.NodeId),
        PathPatterns = NormalizeStringListOrNull(request.PathPatterns),
        Tags = NormalizeStringList(request.Tags),
        ParentId = NormalizeOptionalString(request.ParentId),
        Importance = Math.Clamp(request.Importance, 0, 1),
        ExternalSourceUrl = NormalizeOptionalString(request.ExternalSourceUrl),
        ExternalSourceId = NormalizeOptionalString(request.ExternalSourceId)
    };

    public static async Task<(string? PublishedTargetId, IResult? ErrorResult)> PublishSubmissionAsync(
        ReviewSubmissionRecord submission,
        IMemoryEngine memory)
    {
        switch (submission.Operation)
        {
            case ReviewOperation.Create:
            {
                var request = ReviewJson.Deserialize<RememberRequest>(submission.NormalizedPayloadJson);
                if (request == null)
                    return (null, Results.BadRequest(new { error = $"Submission '{submission.Id}' has no normalized payload." }));

                var created = await memory.RememberAsync(request);
                return (created.Id, null);
            }

            case ReviewOperation.Update:
            {
                if (string.IsNullOrWhiteSpace(submission.TargetId))
                    return (null, Results.BadRequest(new { error = $"Submission '{submission.Id}' is missing targetId." }));

                var request = ReviewJson.Deserialize<RememberRequest>(submission.NormalizedPayloadJson);
                if (request == null)
                    return (null, Results.BadRequest(new { error = $"Submission '{submission.Id}' has no normalized payload." }));

                var current = memory.GetMemoryById(submission.TargetId);
                if (current == null)
                    return (null, Results.Json(new { error = $"Formal memory '{submission.TargetId}' no longer exists." }, statusCode: 409));

                var original = ReviewJson.Deserialize<MemoryEntry>(submission.TargetSnapshotJson);
                if (original != null && current.Version != original.Version)
                {
                    return (null, Results.Json(new
                    {
                        error = $"Formal memory '{submission.TargetId}' changed after submission. Please resubmit against the latest version."
                    }, statusCode: 409));
                }

                await memory.UpdateMemoryAsync(submission.TargetId, request);
                return (submission.TargetId, null);
            }

            case ReviewOperation.Delete:
            {
                if (string.IsNullOrWhiteSpace(submission.TargetId))
                    return (null, Results.BadRequest(new { error = $"Submission '{submission.Id}' is missing targetId." }));

                var current = memory.GetMemoryById(submission.TargetId);
                if (current == null)
                    return (null, Results.Json(new { error = $"Formal memory '{submission.TargetId}' no longer exists." }, statusCode: 409));

                var original = ReviewJson.Deserialize<MemoryEntry>(submission.TargetSnapshotJson);
                if (original != null && current.Version != original.Version)
                {
                    return (null, Results.Json(new
                    {
                        error = $"Formal memory '{submission.TargetId}' changed after submission. Please resubmit against the latest version."
                    }, statusCode: 409));
                }

                var deleted = memory.DeleteMemory(submission.TargetId);
                return deleted
                    ? (submission.TargetId, null)
                    : (null, Results.Json(new { error = $"Failed to delete '{submission.TargetId}'." }, statusCode: 409));
            }

            default:
                return (null, Results.BadRequest(new { error = $"Unsupported operation '{submission.Operation}'." }));
        }
    }

    public static ReviewActionRecord BuildAction(
        string submissionId,
        string action,
        ReviewActor actor,
        ReviewSubmissionStatus? beforeStatus,
        ReviewSubmissionStatus afterStatus,
        string? comment,
        DateTime createdAt) => new()
    {
        Id = NewId(),
        SubmissionId = submissionId,
        Action = action,
        ActorUserId = actor.UserId,
        ActorUsername = actor.Username,
        ActorRole = actor.Role,
        Comment = NormalizeOptionalString(comment),
        BeforeStatus = beforeStatus,
        AfterStatus = afterStatus,
        CreatedAt = createdAt
    };

    public static bool OwnsSubmission(ReviewSubmissionRecord submission, ReviewActor actor)
    {
        return string.Equals(submission.SubmitterUserId, actor.UserId, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(submission.SubmitterUsername, actor.Username, StringComparison.OrdinalIgnoreCase);
    }

    public static object ToSubmissionApiModel(ReviewSubmissionRecord submission, List<ReviewActionRecord>? actions)
    {
        var normalizedPayload = ReviewJson.DeserializeElement(submission.NormalizedPayloadJson);
        var proposedPayload = ReviewJson.DeserializeElement(submission.ProposedPayloadJson);
        var targetSnapshot = ReviewJson.DeserializeElement(submission.TargetSnapshotJson);

        return new
        {
            submission.Id,
            submission.EntityKind,
            submission.Operation,
            submission.TargetId,
            targetSnapshot,
            proposedPayload,
            normalizedPayload,
            submission.Status,
            submitter = new
            {
                userId = submission.SubmitterUserId,
                username = submission.SubmitterUsername,
                role = submission.SubmitterRole
            },
            submission.Source,
            submission.Title,
            submission.Reason,
            submission.ReviewNote,
            submission.CreatedAt,
            submission.UpdatedAt,
            submission.ReviewedAt,
            submission.ReviewedByUserId,
            submission.ReviewedByUsername,
            submission.PublishedAt,
            submission.PublishedByUserId,
            submission.PublishedByUsername,
            submission.PublishedTargetId,
            actions = actions?.Select(action => new
            {
                action.Id,
                action.Action,
                actor = new
                {
                    userId = action.ActorUserId,
                    username = action.ActorUsername,
                    role = action.ActorRole
                },
                action.Comment,
                action.BeforeStatus,
                action.AfterStatus,
                action.CreatedAt
            })
        };
    }

    public static string BuildSubmissionTitle(
        ReviewOperation operation,
        RememberRequest? request,
        string? targetId)
    {
        var summary = NormalizeOptionalString(request?.Summary);
        return operation switch
        {
            ReviewOperation.Create => summary ?? "Create memory submission",
            ReviewOperation.Update => summary ?? $"Update memory {targetId}",
            ReviewOperation.Delete => summary ?? $"Delete memory {targetId}",
            _ => "Memory review submission"
        };
    }

    public static string? NormalizeOptionalString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Trim();
    }

    public static List<string> NormalizeStringList(IEnumerable<string>? values) =>
        values?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList()
        ?? [];

    public static List<string>? NormalizeStringListOrNull(IEnumerable<string>? values)
    {
        var normalized = NormalizeStringList(values);
        return normalized.Count == 0 ? null : normalized;
    }

    public static string NewId() => Guid.NewGuid().ToString("N");
}
