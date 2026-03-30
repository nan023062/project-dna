using System.Security.Claims;
using Dna.Auth;
using Dna.Knowledge;
using Dna.Review;
using Microsoft.AspNetCore.Mvc;

namespace Dna.Interfaces.Api;

public static class ReviewEndpoints
{
    public static void MapReviewEndpoints(this WebApplication app)
    {
        MapSubmissionEndpoints(app);
        MapAdminEndpoints(app);
    }

    private static void MapSubmissionEndpoints(WebApplication app)
    {
        var review = app.MapGroup("/api/review/memory/submissions");

        review.MapPost("/", async (
            MemoryReviewSubmissionRequest request,
            ClaimsPrincipal principal,
            UserStore users,
            IMemoryEngine memory,
            IGraphEngine graph,
            MemoryReviewStore reviewStore) =>
        {
            if (!ReviewAuthorization.TryGetSubmissionActor(principal, users, out var actor))
                return Results.Json(new { error = "Authentication is required for review submissions." }, statusCode: 401);

            var validation = ReviewWorkflow.ValidateSubmissionRequest(request, memory, graph);
            if (validation.ErrorResult is not null)
                return validation.ErrorResult;

            var now = DateTime.UtcNow;
            var submission = new ReviewSubmissionRecord
            {
                Id = ReviewWorkflow.NewId(),
                EntityKind = "memory",
                Operation = validation.Operation,
                TargetId = validation.TargetId,
                TargetSnapshotJson = validation.TargetSnapshotJson,
                ProposedPayloadJson = validation.ProposedPayloadJson,
                NormalizedPayloadJson = validation.NormalizedPayloadJson,
                Status = ReviewSubmissionStatus.Pending,
                SubmitterUserId = actor.UserId,
                SubmitterUsername = actor.Username,
                SubmitterRole = actor.Role,
                Source = actor.Source,
                Title = ReviewWorkflow.BuildSubmissionTitle(validation.Operation, validation.NormalizedRememberRequest, validation.TargetId),
                Reason = ReviewWorkflow.NormalizeOptionalString(request.Reason),
                CreatedAt = now,
                UpdatedAt = now
            };

            reviewStore.CreateSubmission(submission);
            reviewStore.AddAction(ReviewWorkflow.BuildAction(
                submission.Id, "submit", actor, null, ReviewSubmissionStatus.Pending, request.Reason, now));

            return Results.Created($"/api/review/memory/submissions/{submission.Id}",
                ReviewWorkflow.ToSubmissionApiModel(submission, []));
        });

        review.MapGet("/mine", (
            ClaimsPrincipal principal,
            UserStore users,
            MemoryReviewStore reviewStore) =>
        {
            if (!ReviewAuthorization.TryGetSubmissionActor(principal, users, out var actor))
                return Results.Json(new { error = "Authentication is required." }, statusCode: 401);

            var items = reviewStore.ListSubmissionsForUser(actor)
                .Select(item => ReviewWorkflow.ToSubmissionApiModel(item, null));
            return Results.Ok(items);
        });

        review.MapGet("/{id}", (
            string id,
            ClaimsPrincipal principal,
            UserStore users,
            MemoryReviewStore reviewStore) =>
        {
            var submission = reviewStore.GetSubmission(id);
            if (submission == null)
                return Results.NotFound(new { error = $"Review submission '{id}' was not found." });

            var isAdmin = ReviewAuthorization.TryGetAdminActor(principal, users, out _);
            ReviewActor? actor = null;
            if (!isAdmin && !ReviewAuthorization.TryGetSubmissionActor(principal, users, out actor!))
                return Results.Json(new { error = "Authentication is required." }, statusCode: 401);
            if (!isAdmin && actor != null && !ReviewWorkflow.OwnsSubmission(submission, actor))
                return Results.Json(new { error = "You can only view your own submissions." }, statusCode: 403);

            return Results.Ok(ReviewWorkflow.ToSubmissionApiModel(submission, reviewStore.ListActions(id)));
        });

        review.MapPut("/{id}", (
            string id,
            MemoryReviewSubmissionRequest request,
            ClaimsPrincipal principal,
            UserStore users,
            IMemoryEngine memory,
            IGraphEngine graph,
            MemoryReviewStore reviewStore) =>
        {
            if (!ReviewAuthorization.TryGetSubmissionActor(principal, users, out var actor))
                return Results.Json(new { error = "Authentication is required." }, statusCode: 401);

            var current = reviewStore.GetSubmission(id);
            if (current == null)
                return Results.NotFound(new { error = $"Review submission '{id}' was not found." });
            if (!ReviewWorkflow.OwnsSubmission(current, actor))
                return Results.Json(new { error = "You can only edit your own submissions." }, statusCode: 403);
            if (current.Status is not (ReviewSubmissionStatus.Pending or ReviewSubmissionStatus.Rejected))
            {
                return Results.Json(new
                {
                    error = $"Submission '{id}' is in status '{current.Status}' and can no longer be edited."
                }, statusCode: 409);
            }

            var validation = ReviewWorkflow.ValidateSubmissionRequest(request, memory, graph);
            if (validation.ErrorResult is not null)
                return validation.ErrorResult;

            var now = DateTime.UtcNow;
            var updated = new ReviewSubmissionRecord
            {
                Id = current.Id,
                EntityKind = current.EntityKind,
                Operation = validation.Operation,
                TargetId = validation.TargetId,
                TargetSnapshotJson = validation.TargetSnapshotJson,
                ProposedPayloadJson = validation.ProposedPayloadJson,
                NormalizedPayloadJson = validation.NormalizedPayloadJson,
                Status = ReviewSubmissionStatus.Pending,
                SubmitterUserId = current.SubmitterUserId,
                SubmitterUsername = current.SubmitterUsername,
                SubmitterRole = current.SubmitterRole,
                Source = actor.Source,
                Title = ReviewWorkflow.BuildSubmissionTitle(validation.Operation, validation.NormalizedRememberRequest, validation.TargetId),
                Reason = ReviewWorkflow.NormalizeOptionalString(request.Reason),
                CreatedAt = current.CreatedAt,
                UpdatedAt = now
            };

            reviewStore.UpdateSubmission(updated);
            reviewStore.AddAction(ReviewWorkflow.BuildAction(
                updated.Id,
                current.Status == ReviewSubmissionStatus.Rejected ? "resubmit" : "update",
                actor,
                current.Status,
                updated.Status,
                request.Reason,
                now));

            return Results.Ok(ReviewWorkflow.ToSubmissionApiModel(updated, reviewStore.ListActions(id)));
        });

        review.MapDelete("/{id}", (
            string id,
            ClaimsPrincipal principal,
            UserStore users,
            MemoryReviewStore reviewStore) =>
        {
            if (!ReviewAuthorization.TryGetSubmissionActor(principal, users, out var actor))
                return Results.Json(new { error = "Authentication is required." }, statusCode: 401);

            var current = reviewStore.GetSubmission(id);
            if (current == null)
                return Results.NotFound(new { error = $"Review submission '{id}' was not found." });
            if (!ReviewWorkflow.OwnsSubmission(current, actor))
                return Results.Json(new { error = "You can only withdraw your own submissions." }, statusCode: 403);
            if (current.Status is ReviewSubmissionStatus.Published or ReviewSubmissionStatus.Withdrawn)
            {
                return Results.Json(new
                {
                    error = $"Submission '{id}' is in status '{current.Status}' and cannot be withdrawn."
                }, statusCode: 409);
            }

            var now = DateTime.UtcNow;
            var updated = new ReviewSubmissionRecord
            {
                Id = current.Id,
                EntityKind = current.EntityKind,
                Operation = current.Operation,
                TargetId = current.TargetId,
                TargetSnapshotJson = current.TargetSnapshotJson,
                ProposedPayloadJson = current.ProposedPayloadJson,
                NormalizedPayloadJson = current.NormalizedPayloadJson,
                Status = ReviewSubmissionStatus.Withdrawn,
                SubmitterUserId = current.SubmitterUserId,
                SubmitterUsername = current.SubmitterUsername,
                SubmitterRole = current.SubmitterRole,
                Source = current.Source,
                Title = current.Title,
                Reason = current.Reason,
                ReviewNote = current.ReviewNote,
                CreatedAt = current.CreatedAt,
                UpdatedAt = now,
                ReviewedAt = current.ReviewedAt,
                ReviewedByUserId = current.ReviewedByUserId,
                ReviewedByUsername = current.ReviewedByUsername,
                PublishedAt = current.PublishedAt,
                PublishedByUserId = current.PublishedByUserId,
                PublishedByUsername = current.PublishedByUsername,
                PublishedTargetId = current.PublishedTargetId
            };

            reviewStore.UpdateSubmission(updated);
            reviewStore.AddAction(ReviewWorkflow.BuildAction(
                id, "withdraw", actor, current.Status, updated.Status, null, now));
            return Results.Ok(ReviewWorkflow.ToSubmissionApiModel(updated, reviewStore.ListActions(id)));
        });
    }

    private static void MapAdminEndpoints(WebApplication app)
    {
        var admin = app.MapGroup("/api/admin");
        admin.RequireAuthorization();

        admin.MapGet("/review/submissions", (
            string? status,
            string? submitter,
            ClaimsPrincipal principal,
            UserStore users,
            MemoryReviewStore reviewStore) =>
        {
            if (!ReviewAuthorization.TryGetAdminActor(principal, users, out _))
                return Results.Json(new { error = "Admin role required." }, statusCode: 403);

            ReviewSubmissionStatus? parsedStatus = null;
            if (!string.IsNullOrWhiteSpace(status))
            {
                if (!Enum.TryParse<ReviewSubmissionStatus>(status, true, out var value))
                    return Results.BadRequest(new { error = $"Invalid review status '{status}'." });
                parsedStatus = value;
            }

            var items = reviewStore.ListSubmissions(parsedStatus, submitter, "memory")
                .Select(item => ReviewWorkflow.ToSubmissionApiModel(item, null));
            return Results.Ok(items);
        });

        admin.MapGet("/review/submissions/{id}", (
            string id,
            ClaimsPrincipal principal,
            UserStore users,
            MemoryReviewStore reviewStore) =>
        {
            if (!ReviewAuthorization.TryGetAdminActor(principal, users, out _))
                return Results.Json(new { error = "Admin role required." }, statusCode: 403);

            var submission = reviewStore.GetSubmission(id);
            return submission == null
                ? Results.NotFound(new { error = $"Review submission '{id}' was not found." })
                : Results.Ok(ReviewWorkflow.ToSubmissionApiModel(submission, reviewStore.ListActions(id)));
        });

        admin.MapPost("/review/submissions/{id}/approve", (
            string id,
            ReviewDecisionRequest request,
            ClaimsPrincipal principal,
            UserStore users,
            MemoryReviewStore reviewStore) =>
        {
            if (!ReviewAuthorization.TryGetAdminActor(principal, users, out var actor))
                return Results.Json(new { error = "Admin role required." }, statusCode: 403);

            var current = reviewStore.GetSubmission(id);
            if (current == null)
                return Results.NotFound(new { error = $"Review submission '{id}' was not found." });
            if (current.Status != ReviewSubmissionStatus.Pending)
                return Results.Json(new { error = $"Submission '{id}' must be pending before approval." }, statusCode: 409);

            var now = DateTime.UtcNow;
            var updated = new ReviewSubmissionRecord
            {
                Id = current.Id,
                EntityKind = current.EntityKind,
                Operation = current.Operation,
                TargetId = current.TargetId,
                TargetSnapshotJson = current.TargetSnapshotJson,
                ProposedPayloadJson = current.ProposedPayloadJson,
                NormalizedPayloadJson = current.NormalizedPayloadJson,
                Status = ReviewSubmissionStatus.Approved,
                SubmitterUserId = current.SubmitterUserId,
                SubmitterUsername = current.SubmitterUsername,
                SubmitterRole = current.SubmitterRole,
                Source = current.Source,
                Title = current.Title,
                Reason = current.Reason,
                ReviewNote = ReviewWorkflow.NormalizeOptionalString(request.Comment),
                CreatedAt = current.CreatedAt,
                UpdatedAt = now,
                ReviewedAt = now,
                ReviewedByUserId = actor.UserId,
                ReviewedByUsername = actor.Username
            };

            reviewStore.UpdateSubmission(updated);
            reviewStore.AddAction(ReviewWorkflow.BuildAction(
                id, "approve", actor, current.Status, updated.Status, request.Comment, now));
            return Results.Ok(ReviewWorkflow.ToSubmissionApiModel(updated, reviewStore.ListActions(id)));
        });

        admin.MapPost("/review/submissions/{id}/reject", (
            string id,
            ReviewDecisionRequest request,
            ClaimsPrincipal principal,
            UserStore users,
            MemoryReviewStore reviewStore) =>
        {
            if (!ReviewAuthorization.TryGetAdminActor(principal, users, out var actor))
                return Results.Json(new { error = "Admin role required." }, statusCode: 403);

            var current = reviewStore.GetSubmission(id);
            if (current == null)
                return Results.NotFound(new { error = $"Review submission '{id}' was not found." });
            if (current.Status is ReviewSubmissionStatus.Published or ReviewSubmissionStatus.Withdrawn)
                return Results.Json(new { error = $"Submission '{id}' is already '{current.Status}' and cannot be rejected." }, statusCode: 409);

            var now = DateTime.UtcNow;
            var updated = new ReviewSubmissionRecord
            {
                Id = current.Id,
                EntityKind = current.EntityKind,
                Operation = current.Operation,
                TargetId = current.TargetId,
                TargetSnapshotJson = current.TargetSnapshotJson,
                ProposedPayloadJson = current.ProposedPayloadJson,
                NormalizedPayloadJson = current.NormalizedPayloadJson,
                Status = ReviewSubmissionStatus.Rejected,
                SubmitterUserId = current.SubmitterUserId,
                SubmitterUsername = current.SubmitterUsername,
                SubmitterRole = current.SubmitterRole,
                Source = current.Source,
                Title = current.Title,
                Reason = current.Reason,
                ReviewNote = ReviewWorkflow.NormalizeOptionalString(request.Comment),
                CreatedAt = current.CreatedAt,
                UpdatedAt = now,
                ReviewedAt = now,
                ReviewedByUserId = actor.UserId,
                ReviewedByUsername = actor.Username
            };

            reviewStore.UpdateSubmission(updated);
            reviewStore.AddAction(ReviewWorkflow.BuildAction(
                id, "reject", actor, current.Status, updated.Status, request.Comment, now));
            return Results.Ok(ReviewWorkflow.ToSubmissionApiModel(updated, reviewStore.ListActions(id)));
        });

        admin.MapPost("/review/submissions/{id}/publish", async (
            string id,
            ReviewDecisionRequest request,
            ClaimsPrincipal principal,
            UserStore users,
            IMemoryEngine memory,
            MemoryReviewStore reviewStore) =>
        {
            if (!ReviewAuthorization.TryGetAdminActor(principal, users, out var actor))
                return Results.Json(new { error = "Admin role required." }, statusCode: 403);

            var current = reviewStore.GetSubmission(id);
            if (current == null)
                return Results.NotFound(new { error = $"Review submission '{id}' was not found." });
            if (current.Status != ReviewSubmissionStatus.Approved)
                return Results.Json(new { error = $"Submission '{id}' must be approved before publish." }, statusCode: 409);

            var publishResult = await ReviewWorkflow.PublishSubmissionAsync(current, memory);
            if (publishResult.ErrorResult is not null)
                return publishResult.ErrorResult;

            var now = DateTime.UtcNow;
            var updated = new ReviewSubmissionRecord
            {
                Id = current.Id,
                EntityKind = current.EntityKind,
                Operation = current.Operation,
                TargetId = current.TargetId,
                TargetSnapshotJson = current.TargetSnapshotJson,
                ProposedPayloadJson = current.ProposedPayloadJson,
                NormalizedPayloadJson = current.NormalizedPayloadJson,
                Status = ReviewSubmissionStatus.Published,
                SubmitterUserId = current.SubmitterUserId,
                SubmitterUsername = current.SubmitterUsername,
                SubmitterRole = current.SubmitterRole,
                Source = current.Source,
                Title = current.Title,
                Reason = current.Reason,
                ReviewNote = ReviewWorkflow.NormalizeOptionalString(request.Comment) ?? current.ReviewNote,
                CreatedAt = current.CreatedAt,
                UpdatedAt = now,
                ReviewedAt = current.ReviewedAt,
                ReviewedByUserId = current.ReviewedByUserId,
                ReviewedByUsername = current.ReviewedByUsername,
                PublishedAt = now,
                PublishedByUserId = actor.UserId,
                PublishedByUsername = actor.Username,
                PublishedTargetId = publishResult.PublishedTargetId
            };

            reviewStore.UpdateSubmission(updated);
            reviewStore.AddAction(ReviewWorkflow.BuildAction(
                id, "publish", actor, current.Status, updated.Status, request.Comment, now));
            return Results.Ok(new
            {
                submission = ReviewWorkflow.ToSubmissionApiModel(updated, reviewStore.ListActions(id)),
                publishedTargetId = publishResult.PublishedTargetId
            });
        });

        admin.MapPost("/memory/remember", async (
            AdminMemoryWriteRequest request,
            ClaimsPrincipal principal,
            UserStore users,
            IMemoryEngine memory,
            IGraphEngine graph,
            MemoryReviewStore reviewStore) =>
        {
            if (!ReviewAuthorization.TryGetAdminActor(principal, users, out var actor))
                return Results.Json(new { error = "Admin role required." }, statusCode: 403);
            if (request.Memory == null)
                return Results.BadRequest(new { error = "memory is required." });
            if (string.IsNullOrWhiteSpace(request.Reason))
                return Results.BadRequest(new { error = "reason is required." });

            var normalized = ReviewWorkflow.NormalizeRememberRequest(request.Memory);
            var validation = ReviewWorkflow.ValidateNormalizedRememberRequest(normalized, graph);
            if (validation is not null)
                return validation;

            try
            {
                var created = await memory.RememberAsync(normalized);
                reviewStore.AddDirectPublishLog(new DirectPublishLogRecord
                {
                    Id = ReviewWorkflow.NewId(),
                    EntityKind = "memory",
                    Operation = ReviewOperation.Create,
                    TargetId = created.Id,
                    AfterSnapshotJson = ReviewJson.Serialize(created),
                    ActorUserId = actor.UserId,
                    ActorUsername = actor.Username,
                    ActorRole = actor.Role,
                    Reason = request.Reason.Trim(),
                    CreatedAt = DateTime.UtcNow
                });
                return Results.Ok(created);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        admin.MapPut("/memory/{id}", async (
            string id,
            AdminMemoryWriteRequest request,
            ClaimsPrincipal principal,
            UserStore users,
            IMemoryEngine memory,
            IGraphEngine graph,
            MemoryReviewStore reviewStore) =>
        {
            if (!ReviewAuthorization.TryGetAdminActor(principal, users, out var actor))
                return Results.Json(new { error = "Admin role required." }, statusCode: 403);
            if (request.Memory == null)
                return Results.BadRequest(new { error = "memory is required." });
            if (string.IsNullOrWhiteSpace(request.Reason))
                return Results.BadRequest(new { error = "reason is required." });

            var existing = memory.GetMemoryById(id);
            if (existing == null)
                return Results.NotFound(new { error = $"Memory '{id}' was not found." });

            var normalized = ReviewWorkflow.NormalizeRememberRequest(request.Memory);
            var validation = ReviewWorkflow.ValidateNormalizedRememberRequest(normalized, graph);
            if (validation is not null)
                return validation;

            try
            {
                var updatedMemory = await memory.UpdateMemoryAsync(id, normalized);
                reviewStore.AddDirectPublishLog(new DirectPublishLogRecord
                {
                    Id = ReviewWorkflow.NewId(),
                    EntityKind = "memory",
                    Operation = ReviewOperation.Update,
                    TargetId = id,
                    BeforeSnapshotJson = ReviewJson.Serialize(existing),
                    AfterSnapshotJson = ReviewJson.Serialize(updatedMemory),
                    ActorUserId = actor.UserId,
                    ActorUsername = actor.Username,
                    ActorRole = actor.Role,
                    Reason = request.Reason.Trim(),
                    CreatedAt = DateTime.UtcNow
                });
                return Results.Ok(updatedMemory);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        admin.MapDelete("/memory/{id}", (
            string id,
            [FromBody] AdminDeleteMemoryRequest request,
            ClaimsPrincipal principal,
            UserStore users,
            IMemoryEngine memory,
            MemoryReviewStore reviewStore) =>
        {
            if (!ReviewAuthorization.TryGetAdminActor(principal, users, out var actor))
                return Results.Json(new { error = "Admin role required." }, statusCode: 403);
            if (request == null || string.IsNullOrWhiteSpace(request.Reason))
                return Results.BadRequest(new { error = "reason is required." });

            var existing = memory.GetMemoryById(id);
            if (existing == null)
                return Results.NotFound(new { error = $"Memory '{id}' was not found." });

            var deleted = memory.DeleteMemory(id);
            if (!deleted)
                return Results.NotFound(new { error = $"Memory '{id}' was not found." });

            reviewStore.AddDirectPublishLog(new DirectPublishLogRecord
            {
                Id = ReviewWorkflow.NewId(),
                EntityKind = "memory",
                Operation = ReviewOperation.Delete,
                TargetId = id,
                BeforeSnapshotJson = ReviewJson.Serialize(existing),
                ActorUserId = actor.UserId,
                ActorUsername = actor.Username,
                ActorRole = actor.Role,
                Reason = request.Reason.Trim(),
                CreatedAt = DateTime.UtcNow
            });

            return Results.Ok(new { message = $"Deleted '{id}'." });
        });
    }
}
