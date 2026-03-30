using Dna.Memory.Models;

namespace Dna.Review;

internal enum ReviewOperation
{
    Create,
    Update,
    Delete
}

internal enum ReviewSubmissionStatus
{
    Draft,
    Pending,
    Approved,
    Rejected,
    Published,
    Withdrawn,
    Superseded
}

internal sealed class ReviewActor
{
    public string UserId { get; init; } = "";
    public string Username { get; init; } = "";
    public string Role { get; init; } = "editor";
    public bool IsAuthenticated { get; init; }
    public string Source { get; init; } = "api";
}

internal sealed class ReviewSubmissionRecord
{
    public string Id { get; init; } = "";
    public string EntityKind { get; init; } = "memory";
    public ReviewOperation Operation { get; init; }
    public string? TargetId { get; init; }
    public string? TargetSnapshotJson { get; init; }
    public string? ProposedPayloadJson { get; init; }
    public string? NormalizedPayloadJson { get; init; }
    public ReviewSubmissionStatus Status { get; init; } = ReviewSubmissionStatus.Pending;
    public string SubmitterUserId { get; init; } = "";
    public string SubmitterUsername { get; init; } = "";
    public string SubmitterRole { get; init; } = "editor";
    public string Source { get; init; } = "api";
    public string? Title { get; init; }
    public string? Reason { get; init; }
    public string? ReviewNote { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public DateTime? ReviewedAt { get; init; }
    public string? ReviewedByUserId { get; init; }
    public string? ReviewedByUsername { get; init; }
    public DateTime? PublishedAt { get; init; }
    public string? PublishedByUserId { get; init; }
    public string? PublishedByUsername { get; init; }
    public string? PublishedTargetId { get; init; }
}

internal sealed class ReviewActionRecord
{
    public string Id { get; init; } = "";
    public string SubmissionId { get; init; } = "";
    public string Action { get; init; } = "";
    public string ActorUserId { get; init; } = "";
    public string ActorUsername { get; init; } = "";
    public string ActorRole { get; init; } = "editor";
    public string? Comment { get; init; }
    public ReviewSubmissionStatus? BeforeStatus { get; init; }
    public ReviewSubmissionStatus AfterStatus { get; init; }
    public DateTime CreatedAt { get; init; }
}

internal sealed class DirectPublishLogRecord
{
    public string Id { get; init; } = "";
    public string EntityKind { get; init; } = "memory";
    public ReviewOperation Operation { get; init; }
    public string? TargetId { get; init; }
    public string? BeforeSnapshotJson { get; init; }
    public string? AfterSnapshotJson { get; init; }
    public string ActorUserId { get; init; } = "";
    public string ActorUsername { get; init; } = "";
    public string ActorRole { get; init; } = "admin";
    public string Reason { get; init; } = "";
    public DateTime CreatedAt { get; init; }
}

public sealed class MemoryReviewSubmissionRequest
{
    public string Operation { get; init; } = "create";
    public string? TargetId { get; init; }
    public RememberRequest? Memory { get; init; }
    public string? Reason { get; init; }
}

public sealed class ReviewDecisionRequest
{
    public string? Comment { get; init; }
}

public sealed class AdminMemoryWriteRequest
{
    public string Reason { get; init; } = "";
    public RememberRequest? Memory { get; init; }
}

public sealed class AdminDeleteMemoryRequest
{
    public string Reason { get; init; } = "";
}
