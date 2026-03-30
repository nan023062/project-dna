using Microsoft.Data.Sqlite;

namespace Dna.Review;

internal sealed class MemoryReviewStore : IDisposable
{
    private SqliteConnection? _db;

    public void Initialize(string dataDir)
    {
        Directory.CreateDirectory(dataDir);
        var dbPath = Path.Combine(dataDir, "review.db");
        _db = new SqliteConnection($"Data Source={dbPath}");
        _db.Open();

        using var cmd = _db.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA busy_timeout=5000;

            CREATE TABLE IF NOT EXISTS review_submissions (
                id                      TEXT PRIMARY KEY,
                entity_kind             TEXT NOT NULL,
                operation               TEXT NOT NULL,
                target_id               TEXT,
                target_snapshot_json    TEXT,
                proposed_payload_json   TEXT,
                normalized_payload_json TEXT,
                status                  TEXT NOT NULL,
                submitter_user_id       TEXT NOT NULL,
                submitter_username      TEXT NOT NULL,
                submitter_role          TEXT NOT NULL,
                source                  TEXT NOT NULL,
                title                   TEXT,
                reason                  TEXT,
                review_note             TEXT,
                created_at              TEXT NOT NULL,
                updated_at              TEXT NOT NULL,
                reviewed_at             TEXT,
                reviewed_by_user_id     TEXT,
                reviewed_by_username    TEXT,
                published_at            TEXT,
                published_by_user_id    TEXT,
                published_by_username   TEXT,
                published_target_id     TEXT
            );

            CREATE INDEX IF NOT EXISTS idx_review_status ON review_submissions(status);
            CREATE INDEX IF NOT EXISTS idx_review_submitter ON review_submissions(submitter_user_id, created_at DESC);
            CREATE INDEX IF NOT EXISTS idx_review_target ON review_submissions(target_id);
            CREATE INDEX IF NOT EXISTS idx_review_entity ON review_submissions(entity_kind, created_at DESC);

            CREATE TABLE IF NOT EXISTS review_actions (
                id              TEXT PRIMARY KEY,
                submission_id   TEXT NOT NULL,
                action          TEXT NOT NULL,
                actor_user_id   TEXT NOT NULL,
                actor_username  TEXT NOT NULL,
                actor_role      TEXT NOT NULL,
                comment         TEXT,
                before_status   TEXT,
                after_status    TEXT NOT NULL,
                created_at      TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_review_actions_submission ON review_actions(submission_id, created_at DESC);

            CREATE TABLE IF NOT EXISTS direct_publish_logs (
                id                    TEXT PRIMARY KEY,
                entity_kind           TEXT NOT NULL,
                operation             TEXT NOT NULL,
                target_id             TEXT,
                before_snapshot_json  TEXT,
                after_snapshot_json   TEXT,
                actor_user_id         TEXT NOT NULL,
                actor_username        TEXT NOT NULL,
                actor_role            TEXT NOT NULL,
                reason                TEXT NOT NULL,
                created_at            TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }

    public ReviewSubmissionRecord CreateSubmission(ReviewSubmissionRecord record)
    {
        using var cmd = _db!.CreateCommand();
        cmd.CommandText = """
            INSERT INTO review_submissions (
                id, entity_kind, operation, target_id, target_snapshot_json,
                proposed_payload_json, normalized_payload_json, status,
                submitter_user_id, submitter_username, submitter_role, source,
                title, reason, review_note, created_at, updated_at,
                reviewed_at, reviewed_by_user_id, reviewed_by_username,
                published_at, published_by_user_id, published_by_username, published_target_id)
            VALUES (
                @id, @entity_kind, @operation, @target_id, @target_snapshot_json,
                @proposed_payload_json, @normalized_payload_json, @status,
                @submitter_user_id, @submitter_username, @submitter_role, @source,
                @title, @reason, @review_note, @created_at, @updated_at,
                @reviewed_at, @reviewed_by_user_id, @reviewed_by_username,
                @published_at, @published_by_user_id, @published_by_username, @published_target_id)
            """;
        BindSubmission(cmd, record);
        cmd.ExecuteNonQuery();
        return record;
    }

    public ReviewSubmissionRecord UpdateSubmission(ReviewSubmissionRecord record)
    {
        using var cmd = _db!.CreateCommand();
        cmd.CommandText = """
            UPDATE review_submissions
            SET entity_kind = @entity_kind,
                operation = @operation,
                target_id = @target_id,
                target_snapshot_json = @target_snapshot_json,
                proposed_payload_json = @proposed_payload_json,
                normalized_payload_json = @normalized_payload_json,
                status = @status,
                submitter_user_id = @submitter_user_id,
                submitter_username = @submitter_username,
                submitter_role = @submitter_role,
                source = @source,
                title = @title,
                reason = @reason,
                review_note = @review_note,
                created_at = @created_at,
                updated_at = @updated_at,
                reviewed_at = @reviewed_at,
                reviewed_by_user_id = @reviewed_by_user_id,
                reviewed_by_username = @reviewed_by_username,
                published_at = @published_at,
                published_by_user_id = @published_by_user_id,
                published_by_username = @published_by_username,
                published_target_id = @published_target_id
            WHERE id = @id
            """;
        BindSubmission(cmd, record);
        cmd.ExecuteNonQuery();
        return record;
    }

    public ReviewSubmissionRecord? GetSubmission(string id)
    {
        using var cmd = _db!.CreateCommand();
        cmd.CommandText = "SELECT * FROM review_submissions WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? ReadSubmission(reader) : null;
    }

    public List<ReviewSubmissionRecord> ListSubmissions(
        ReviewSubmissionStatus? status = null,
        string? submitter = null,
        string? entityKind = null)
    {
        var conditions = new List<string>();
        using var cmd = _db!.CreateCommand();

        if (status.HasValue)
        {
            conditions.Add("status = @status");
            cmd.Parameters.AddWithValue("@status", status.Value.ToString());
        }

        if (!string.IsNullOrWhiteSpace(submitter))
        {
            conditions.Add("(submitter_user_id = @submitter OR submitter_username = @submitter)");
            cmd.Parameters.AddWithValue("@submitter", submitter);
        }

        if (!string.IsNullOrWhiteSpace(entityKind))
        {
            conditions.Add("entity_kind = @entity_kind");
            cmd.Parameters.AddWithValue("@entity_kind", entityKind);
        }

        var where = conditions.Count == 0 ? "" : $"WHERE {string.Join(" AND ", conditions)}";
        cmd.CommandText = $"SELECT * FROM review_submissions {where} ORDER BY created_at DESC";
        return ReadSubmissions(cmd);
    }

    public List<ReviewSubmissionRecord> ListSubmissionsForUser(ReviewActor actor)
    {
        using var cmd = _db!.CreateCommand();
        cmd.CommandText = """
            SELECT * FROM review_submissions
            WHERE submitter_user_id = @user_id OR submitter_username = @username
            ORDER BY created_at DESC
            """;
        cmd.Parameters.AddWithValue("@user_id", actor.UserId);
        cmd.Parameters.AddWithValue("@username", actor.Username);
        return ReadSubmissions(cmd);
    }

    public void AddAction(ReviewActionRecord record)
    {
        using var cmd = _db!.CreateCommand();
        cmd.CommandText = """
            INSERT INTO review_actions (
                id, submission_id, action, actor_user_id, actor_username,
                actor_role, comment, before_status, after_status, created_at)
            VALUES (
                @id, @submission_id, @action, @actor_user_id, @actor_username,
                @actor_role, @comment, @before_status, @after_status, @created_at)
            """;
        cmd.Parameters.AddWithValue("@id", record.Id);
        cmd.Parameters.AddWithValue("@submission_id", record.SubmissionId);
        cmd.Parameters.AddWithValue("@action", record.Action);
        cmd.Parameters.AddWithValue("@actor_user_id", record.ActorUserId);
        cmd.Parameters.AddWithValue("@actor_username", record.ActorUsername);
        cmd.Parameters.AddWithValue("@actor_role", record.ActorRole);
        cmd.Parameters.AddWithValue("@comment", DbValue(record.Comment));
        cmd.Parameters.AddWithValue("@before_status", record.BeforeStatus?.ToString() is { } before
            ? before
            : DBNull.Value);
        cmd.Parameters.AddWithValue("@after_status", record.AfterStatus.ToString());
        cmd.Parameters.AddWithValue("@created_at", record.CreatedAt.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public List<ReviewActionRecord> ListActions(string submissionId)
    {
        using var cmd = _db!.CreateCommand();
        cmd.CommandText = """
            SELECT id, submission_id, action, actor_user_id, actor_username,
                   actor_role, comment, before_status, after_status, created_at
            FROM review_actions
            WHERE submission_id = @submission_id
            ORDER BY created_at DESC
            """;
        cmd.Parameters.AddWithValue("@submission_id", submissionId);

        var actions = new List<ReviewActionRecord>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            actions.Add(ReadAction(reader));
        return actions;
    }

    public void AddDirectPublishLog(DirectPublishLogRecord record)
    {
        using var cmd = _db!.CreateCommand();
        cmd.CommandText = """
            INSERT INTO direct_publish_logs (
                id, entity_kind, operation, target_id, before_snapshot_json,
                after_snapshot_json, actor_user_id, actor_username, actor_role,
                reason, created_at)
            VALUES (
                @id, @entity_kind, @operation, @target_id, @before_snapshot_json,
                @after_snapshot_json, @actor_user_id, @actor_username, @actor_role,
                @reason, @created_at)
            """;
        cmd.Parameters.AddWithValue("@id", record.Id);
        cmd.Parameters.AddWithValue("@entity_kind", record.EntityKind);
        cmd.Parameters.AddWithValue("@operation", record.Operation.ToString());
        cmd.Parameters.AddWithValue("@target_id", DbValue(record.TargetId));
        cmd.Parameters.AddWithValue("@before_snapshot_json", DbValue(record.BeforeSnapshotJson));
        cmd.Parameters.AddWithValue("@after_snapshot_json", DbValue(record.AfterSnapshotJson));
        cmd.Parameters.AddWithValue("@actor_user_id", record.ActorUserId);
        cmd.Parameters.AddWithValue("@actor_username", record.ActorUsername);
        cmd.Parameters.AddWithValue("@actor_role", record.ActorRole);
        cmd.Parameters.AddWithValue("@reason", record.Reason);
        cmd.Parameters.AddWithValue("@created_at", record.CreatedAt.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    private static object DbValue(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;

    private static void BindSubmission(SqliteCommand cmd, ReviewSubmissionRecord record)
    {
        cmd.Parameters.AddWithValue("@id", record.Id);
        cmd.Parameters.AddWithValue("@entity_kind", record.EntityKind);
        cmd.Parameters.AddWithValue("@operation", record.Operation.ToString());
        cmd.Parameters.AddWithValue("@target_id", DbValue(record.TargetId));
        cmd.Parameters.AddWithValue("@target_snapshot_json", DbValue(record.TargetSnapshotJson));
        cmd.Parameters.AddWithValue("@proposed_payload_json", DbValue(record.ProposedPayloadJson));
        cmd.Parameters.AddWithValue("@normalized_payload_json", DbValue(record.NormalizedPayloadJson));
        cmd.Parameters.AddWithValue("@status", record.Status.ToString());
        cmd.Parameters.AddWithValue("@submitter_user_id", record.SubmitterUserId);
        cmd.Parameters.AddWithValue("@submitter_username", record.SubmitterUsername);
        cmd.Parameters.AddWithValue("@submitter_role", record.SubmitterRole);
        cmd.Parameters.AddWithValue("@source", record.Source);
        cmd.Parameters.AddWithValue("@title", DbValue(record.Title));
        cmd.Parameters.AddWithValue("@reason", DbValue(record.Reason));
        cmd.Parameters.AddWithValue("@review_note", DbValue(record.ReviewNote));
        cmd.Parameters.AddWithValue("@created_at", record.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@updated_at", record.UpdatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@reviewed_at", record.ReviewedAt?.ToString("O") is { } reviewedAt
            ? reviewedAt
            : DBNull.Value);
        cmd.Parameters.AddWithValue("@reviewed_by_user_id", DbValue(record.ReviewedByUserId));
        cmd.Parameters.AddWithValue("@reviewed_by_username", DbValue(record.ReviewedByUsername));
        cmd.Parameters.AddWithValue("@published_at", record.PublishedAt?.ToString("O") is { } publishedAt
            ? publishedAt
            : DBNull.Value);
        cmd.Parameters.AddWithValue("@published_by_user_id", DbValue(record.PublishedByUserId));
        cmd.Parameters.AddWithValue("@published_by_username", DbValue(record.PublishedByUsername));
        cmd.Parameters.AddWithValue("@published_target_id", DbValue(record.PublishedTargetId));
    }

    private List<ReviewSubmissionRecord> ReadSubmissions(SqliteCommand cmd)
    {
        var submissions = new List<ReviewSubmissionRecord>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            submissions.Add(ReadSubmission(reader));
        return submissions;
    }

    private static ReviewSubmissionRecord ReadSubmission(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(reader.GetOrdinal("id")),
        EntityKind = reader.GetString(reader.GetOrdinal("entity_kind")),
        Operation = Enum.Parse<ReviewOperation>(reader.GetString(reader.GetOrdinal("operation")), true),
        TargetId = ReadNullableString(reader, "target_id"),
        TargetSnapshotJson = ReadNullableString(reader, "target_snapshot_json"),
        ProposedPayloadJson = ReadNullableString(reader, "proposed_payload_json"),
        NormalizedPayloadJson = ReadNullableString(reader, "normalized_payload_json"),
        Status = Enum.Parse<ReviewSubmissionStatus>(reader.GetString(reader.GetOrdinal("status")), true),
        SubmitterUserId = reader.GetString(reader.GetOrdinal("submitter_user_id")),
        SubmitterUsername = reader.GetString(reader.GetOrdinal("submitter_username")),
        SubmitterRole = reader.GetString(reader.GetOrdinal("submitter_role")),
        Source = reader.GetString(reader.GetOrdinal("source")),
        Title = ReadNullableString(reader, "title"),
        Reason = ReadNullableString(reader, "reason"),
        ReviewNote = ReadNullableString(reader, "review_note"),
        CreatedAt = ReadDateTime(reader, "created_at"),
        UpdatedAt = ReadDateTime(reader, "updated_at"),
        ReviewedAt = ReadNullableDateTime(reader, "reviewed_at"),
        ReviewedByUserId = ReadNullableString(reader, "reviewed_by_user_id"),
        ReviewedByUsername = ReadNullableString(reader, "reviewed_by_username"),
        PublishedAt = ReadNullableDateTime(reader, "published_at"),
        PublishedByUserId = ReadNullableString(reader, "published_by_user_id"),
        PublishedByUsername = ReadNullableString(reader, "published_by_username"),
        PublishedTargetId = ReadNullableString(reader, "published_target_id")
    };

    private static ReviewActionRecord ReadAction(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0),
        SubmissionId = reader.GetString(1),
        Action = reader.GetString(2),
        ActorUserId = reader.GetString(3),
        ActorUsername = reader.GetString(4),
        ActorRole = reader.GetString(5),
        Comment = reader.IsDBNull(6) ? null : reader.GetString(6),
        BeforeStatus = reader.IsDBNull(7)
            ? null
            : Enum.Parse<ReviewSubmissionStatus>(reader.GetString(7), true),
        AfterStatus = Enum.Parse<ReviewSubmissionStatus>(reader.GetString(8), true),
        CreatedAt = DateTime.TryParse(reader.GetString(9), out var createdAt) ? createdAt : DateTime.UtcNow
    };

    private static string? ReadNullableString(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static DateTime ReadDateTime(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return DateTime.TryParse(reader.GetString(ordinal), out var value) ? value : DateTime.UtcNow;
    }

    private static DateTime? ReadNullableDateTime(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        if (reader.IsDBNull(ordinal))
            return null;

        return DateTime.TryParse(reader.GetString(ordinal), out var value) ? value : null;
    }

    public void Dispose()
    {
        _db?.Dispose();
        _db = null;
        GC.SuppressFinalize(this);
    }
}
