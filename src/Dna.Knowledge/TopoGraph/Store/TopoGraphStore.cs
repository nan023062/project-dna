using System.Text.Json;
using System.Text.Json.Serialization;
using Dna.Knowledge.FileProtocol;
using Dna.Memory.Store;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dna.Knowledge;

public sealed class TopoGraphStore : ITopoGraphStore
{
    private readonly ILogger<TopoGraphStore> _logger;
    private readonly object _lock = new();
    private readonly KnowledgeFileStore _fileStore = new();

    private ComputedManifest _computed = new();
    private Dictionary<string, string> _nodeAliases = new(StringComparer.OrdinalIgnoreCase);
    private string _storePath = string.Empty;
    private string _graphDbPath = string.Empty;
    private bool _initialized;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public TopoGraphStore(IServiceProvider provider)
    {
        var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger<TopoGraphStore>();
    }

    public void Initialize(string storePath)
    {
        if (_initialized && string.Equals(_storePath, storePath, StringComparison.OrdinalIgnoreCase))
            return;

        _storePath = storePath;
        _graphDbPath = Path.Combine(storePath, "graph.db");
        ReloadLocked();
        _initialized = true;
    }

    public void Reload()
    {
        if (!_initialized)
            return;

        lock (_lock)
            ReloadLocked();
    }

    public ComputedManifest GetComputedManifest()
    {
        lock (_lock)
        {
            return new ComputedManifest
            {
                ModuleDependencies = _computed.ModuleDependencies.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value.ToList(),
                    StringComparer.OrdinalIgnoreCase)
            };
        }
    }

    public Dictionary<string, NodeKnowledge> LoadNodeKnowledgeMap()
    {
        lock (_lock)
        {
            EnsureGraphSchemaLocked();
            var map = new Dictionary<string, NodeKnowledge>(StringComparer.OrdinalIgnoreCase);

            using var conn = CreateGraphConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT node_id, identity, lessons_json, active_tasks_json, facts_json, total_memory_count, identity_memory_id, upgrade_trail_memory_id, memory_ids_json
                FROM graph_node_knowledge
                """;

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var nodeId = reader.GetString(0);
                map[nodeId] = new NodeKnowledge
                {
                    Identity = reader.IsDBNull(1) ? null : reader.GetString(1),
                    Lessons = DeserializeOrDefault(reader.IsDBNull(2) ? "[]" : reader.GetString(2), new List<LessonSummary>()),
                    ActiveTasks = DeserializeOrDefault(reader.IsDBNull(3) ? "[]" : reader.GetString(3), new List<string>()),
                    Facts = DeserializeOrDefault(reader.IsDBNull(4) ? "[]" : reader.GetString(4), new List<string>()),
                    TotalMemoryCount = reader.IsDBNull(5) ? 0 : reader.GetInt32(5),
                    IdentityMemoryId = reader.IsDBNull(6) ? null : reader.GetString(6),
                    UpgradeTrailMemoryId = reader.IsDBNull(7) ? null : reader.GetString(7),
                    MemoryIds = DeserializeOrDefault(reader.IsDBNull(8) ? "[]" : reader.GetString(8), new List<string>())
                };
            }

            return map;
        }
    }

    public void UpsertNodeKnowledge(string nodeId, NodeKnowledge knowledge)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
            throw new ArgumentException("nodeId 不能为空", nameof(nodeId));

        lock (_lock)
        {
            EnsureGraphSchemaLocked();
            using var conn = CreateGraphConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO graph_node_knowledge
                    (node_id, identity, lessons_json, active_tasks_json, facts_json, total_memory_count, identity_memory_id, upgrade_trail_memory_id, memory_ids_json, updated_at)
                VALUES
                    (@node, @identity, @lessons, @tasks, @facts, @total, @identityMemoryId, @upgradeTrailMemoryId, @memoryIds, @updatedAt)
                ON CONFLICT(node_id) DO UPDATE SET
                    identity = excluded.identity,
                    lessons_json = excluded.lessons_json,
                    active_tasks_json = excluded.active_tasks_json,
                    facts_json = excluded.facts_json,
                    total_memory_count = excluded.total_memory_count,
                    identity_memory_id = excluded.identity_memory_id,
                    upgrade_trail_memory_id = excluded.upgrade_trail_memory_id,
                    memory_ids_json = excluded.memory_ids_json,
                    updated_at = excluded.updated_at
                """;
            cmd.Parameters.AddWithValue("@node", nodeId);
            cmd.Parameters.AddWithValue("@identity", (object?)knowledge.Identity ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@lessons", JsonSerializer.Serialize(knowledge.Lessons ?? [], JsonOpts));
            cmd.Parameters.AddWithValue("@tasks", JsonSerializer.Serialize(knowledge.ActiveTasks ?? [], JsonOpts));
            cmd.Parameters.AddWithValue("@facts", JsonSerializer.Serialize(knowledge.Facts ?? [], JsonOpts));
            cmd.Parameters.AddWithValue("@total", Math.Max(knowledge.TotalMemoryCount, 0));
            cmd.Parameters.AddWithValue("@identityMemoryId", (object?)knowledge.IdentityMemoryId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@upgradeTrailMemoryId", (object?)knowledge.UpgradeTrailMemoryId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@memoryIds", JsonSerializer.Serialize(knowledge.MemoryIds ?? [], JsonOpts));
            cmd.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();

            SyncNodeKnowledgeToFileProtocolLocked(nodeId, knowledge);
        }
    }

    public List<string> ResolveNodeIdCandidates(string? nodeId, bool strict = false)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
            return [];

        var input = nodeId.Trim();

        lock (_lock)
        {
            if (_nodeAliases.TryGetValue(input, out var resolved))
                return string.Equals(resolved, input, StringComparison.OrdinalIgnoreCase)
                    ? [resolved]
                    : [resolved, input];
        }

        if (strict)
            throw new InvalidOperationException($"nodeId '{input}' 不存在于当前 TopoGraph 定义中。");

        return [input];
    }

    public void UpdateComputedDependencies(string moduleName, List<string> computedDependencies)
    {
        lock (_lock)
        {
            EnsureGraphSchemaLocked();
            _computed.ModuleDependencies[moduleName] = computedDependencies
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            using var conn = CreateGraphConnection();
            using var tx = conn.BeginTransaction();

            using (var delete = conn.CreateCommand())
            {
                delete.Transaction = tx;
                delete.CommandText = "DELETE FROM graph_computed_dependencies WHERE module_name = @module";
                delete.Parameters.AddWithValue("@module", moduleName);
                delete.ExecuteNonQuery();
            }

            using (var insert = conn.CreateCommand())
            {
                insert.Transaction = tx;
                insert.CommandText = """
                    INSERT INTO graph_computed_dependencies (module_name, dependencies_json)
                    VALUES (@module, @deps)
                    """;
                insert.Parameters.AddWithValue("@module", moduleName);
                insert.Parameters.AddWithValue("@deps", JsonSerializer.Serialize(_computed.ModuleDependencies[moduleName], JsonOpts));
                insert.ExecuteNonQuery();
            }

            tx.Commit();
        }
    }

    private void ReloadLocked()
    {
        EnsureGraphSchemaLocked();
        LoadComputedDependenciesLocked();
        RebuildNodeAliasIndexLocked();
    }

    private void LoadComputedDependenciesLocked()
    {
        _computed = new ComputedManifest();

        using var conn = CreateGraphConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT module_name, dependencies_json FROM graph_computed_dependencies";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            _computed.ModuleDependencies[reader.GetString(0)] =
                DeserializeOrDefault(reader.GetString(1), new List<string>());
        }
    }

    private void RebuildNodeAliasIndexLocked()
    {
        _nodeAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var agenticOsPath = ResolveAgenticOsPathFromStore(_storePath);
        if (string.IsNullOrWhiteSpace(agenticOsPath))
            return;

        var definition = _fileStore.LoadAsDefinition(agenticOsPath);
        AddAlias(definition.Project?.Id, definition.Project?.Id);
        AddAlias(definition.Project?.Name, definition.Project?.Id);

        foreach (var department in definition.Departments)
        {
            AddAlias(department.Id, department.Id);
            AddAlias(department.Name, department.Id);
            AddAlias(department.DisciplineCode, department.Id);
        }

        foreach (var module in definition.TechnicalNodes.Cast<TopoGraph.Models.Registrations.TopologyNodeRegistration>().Concat(definition.TeamNodes))
        {
            AddAlias(module.Id, module.Id);
            AddAlias(module.Name, module.Id);
        }
    }

    private void AddAlias(string? alias, string? targetId)
    {
        if (string.IsNullOrWhiteSpace(alias) || string.IsNullOrWhiteSpace(targetId))
            return;

        _nodeAliases[alias.Trim()] = targetId.Trim();
    }

    private void EnsureGraphSchemaLocked()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_graphDbPath) ?? _storePath);

        using var conn = CreateGraphConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS graph_computed_dependencies (
                module_name TEXT PRIMARY KEY,
                dependencies_json TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS graph_node_knowledge (
                node_id TEXT PRIMARY KEY,
                identity TEXT NULL,
                lessons_json TEXT NOT NULL DEFAULT '[]',
                active_tasks_json TEXT NOT NULL DEFAULT '[]',
                facts_json TEXT NOT NULL DEFAULT '[]',
                total_memory_count INTEGER NOT NULL DEFAULT 0,
                identity_memory_id TEXT NULL,
                upgrade_trail_memory_id TEXT NULL,
                memory_ids_json TEXT NOT NULL DEFAULT '[]',
                updated_at TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();

        EnsureGraphNodeKnowledgeColumnLocked(conn, "identity_memory_id", "TEXT NULL");
        EnsureGraphNodeKnowledgeColumnLocked(conn, "upgrade_trail_memory_id", "TEXT NULL");
    }

    private SqliteConnection CreateGraphConnection()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _graphDbPath,
            Mode = SqliteOpenMode.ReadWriteCreate
        };
        var conn = new SqliteConnection(builder.ConnectionString);
        conn.Open();
        return conn;
    }

    private static T DeserializeOrDefault<T>(string json, T fallback)
    {
        try
        {
            var value = JsonSerializer.Deserialize<T>(json, JsonOpts);
            return value == null ? fallback : value;
        }
        catch
        {
            return fallback;
        }
    }

    private static void EnsureGraphNodeKnowledgeColumnLocked(SqliteConnection conn, string columnName, string columnType)
    {
        using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA table_info(graph_node_knowledge)";
        using var reader = pragma.ExecuteReader();

        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                return;
        }

        using var alter = conn.CreateCommand();
        alter.CommandText = $"ALTER TABLE graph_node_knowledge ADD COLUMN {columnName} {columnType}";
        alter.ExecuteNonQuery();
    }

    private static string? ResolveAgenticOsPathFromStore(string storePath)
    {
        if (string.IsNullOrWhiteSpace(storePath))
            return null;

        var candidates = new[]
        {
            storePath,
            Path.GetDirectoryName(storePath),
            Path.Combine(storePath, FileProtocolPaths.AgenticOsDir)
        };

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            var modulesDir = FileProtocolPaths.GetModulesRoot(candidate);
            if (Directory.Exists(modulesDir))
                return candidate;
        }

        return null;
    }

    private void SyncNodeKnowledgeToFileProtocolLocked(string nodeId, NodeKnowledge knowledge)
    {
        var agenticOsPath = ResolveAgenticOsPathFromStore(_storePath);
        if (string.IsNullOrWhiteSpace(agenticOsPath))
            return;

        var module = _fileStore.LoadModule(agenticOsPath, nodeId);
        if (module == null)
            return;

        var dependencies = _fileStore.LoadDependencies(agenticOsPath, nodeId);
        _fileStore.SaveModule(agenticOsPath, module, BuildIdentityMarkdown(knowledge), dependencies);
    }

    private static string BuildIdentityMarkdown(NodeKnowledge knowledge)
    {
        var lines = new List<string>
        {
            "## Summary",
            string.Empty,
            string.IsNullOrWhiteSpace(knowledge.Identity) ? "No summary yet." : knowledge.Identity.Trim()
        };

        if (knowledge.Lessons.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("## Lessons");
            lines.Add(string.Empty);
            foreach (var lesson in knowledge.Lessons.Where(item => !string.IsNullOrWhiteSpace(item.Title)))
            {
                var suffix = string.IsNullOrWhiteSpace(lesson.Resolution)
                    ? string.Empty
                    : $" - {lesson.Resolution!.Trim()}";
                lines.Add($"- {lesson.Title.Trim()}{suffix}");
            }
        }

        if (knowledge.ActiveTasks.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("## Active Tasks");
            lines.Add(string.Empty);
            foreach (var task in knowledge.ActiveTasks.Where(item => !string.IsNullOrWhiteSpace(item)))
                lines.Add($"- {task.Trim()}");
        }

        if (knowledge.Facts.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("## Facts");
            lines.Add(string.Empty);
            foreach (var fact in knowledge.Facts.Where(item => !string.IsNullOrWhiteSpace(item)))
                lines.Add($"- {fact.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(knowledge.IdentityMemoryId) ||
            !string.IsNullOrWhiteSpace(knowledge.UpgradeTrailMemoryId) ||
            knowledge.TotalMemoryCount > 0 ||
            knowledge.MemoryIds.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("## Governance");
            lines.Add(string.Empty);

            if (!string.IsNullOrWhiteSpace(knowledge.IdentityMemoryId))
                lines.Add($"- Identity Memory: `{knowledge.IdentityMemoryId!.Trim()}`");

            if (!string.IsNullOrWhiteSpace(knowledge.UpgradeTrailMemoryId))
                lines.Add($"- Upgrade Trail: `{knowledge.UpgradeTrailMemoryId!.Trim()}`");

            if (knowledge.TotalMemoryCount > 0)
                lines.Add($"- Source Count: {knowledge.TotalMemoryCount}");

            if (knowledge.MemoryIds.Count > 0)
            {
                lines.Add("- Referenced Memories:");
                foreach (var memoryId in knowledge.MemoryIds.Take(20))
                    lines.Add($"  - `{memoryId}`");

                if (knowledge.MemoryIds.Count > 20)
                    lines.Add($"  - ... (+{knowledge.MemoryIds.Count - 20} more)");
            }
        }

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }
}
