using System.Text.Json;
using System.Text.Json.Serialization;
using Dna.Knowledge.Models;
using Dna.Memory.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dna.Memory.Store;

/// <summary>
/// 记忆存储 — SQLite 索引 + JSON 文件双引擎。
/// SQLite (index.db) 存储索引/元数据/向量/FTS，加入 .gitignore，启动时可从 JSON 重建。
/// JSON 文件 (entries/*.json) 是 Git 友好的知识内容，按年月分目录。
/// </summary>
internal partial class MemoryStore : IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly ILogger<MemoryStore> _logger;
    private SqliteConnection? _db;
    private string _projectRoot = string.Empty;
    private string _storePath = string.Empty;
    public string ProjectRoot => _projectRoot;
    public string StorePath => _storePath;
    private string _memoryDir = string.Empty;

    public MemoryStore(IServiceProvider provider)
    {
        var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger<MemoryStore>();
    }

    // ═══════════════════════════════════════════
    //  初始化
    // ═══════════════════════════════════════════

    private bool _initialized;

    public void Reload()
    {
        if (!_initialized) return;
        LoadAllManifests();
        _logger.LogInformation("MemoryStore 已重载配置");
    }

    public void Initialize(string projectRoot, string storePath)
    {
        if (_initialized && _projectRoot == projectRoot && _storePath == storePath) return;

        _projectRoot = projectRoot;
        _storePath = storePath;
        _memoryDir = Path.Combine(storePath, "memory");
        Directory.CreateDirectory(_memoryDir);

        _db?.Dispose();
        var dbPath = Path.Combine(_memoryDir, "index.db");
        _db = new SqliteConnection($"Data Source={dbPath}");
        _db.Open();

        using (var pragma = _db.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;";
            pragma.ExecuteNonQuery();
        }

        EnsureSchema();
        LoadAllManifests();

        var jsonCount = RebuildFromJsonIfNeeded();
        _initialized = true;
        _logger.LogInformation("MemoryStore 已初始化: db={DbPath}, store={Store}, entries={Count}", dbPath, storePath, jsonCount);
    }

    private void EnsureSchema()
    {
        using var cmd = _db!.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS memory_entries (
                id              TEXT PRIMARY KEY,
                type            TEXT NOT NULL,
                layer           TEXT NOT NULL,
                source          TEXT NOT NULL,
                content         TEXT NOT NULL DEFAULT '',
                summary         TEXT,
                importance      REAL DEFAULT 0.5,
                freshness       TEXT DEFAULT 'Fresh',
                created_at      TEXT NOT NULL,
                last_verified_at TEXT,
                stale_after     TEXT,
                superseded_by   TEXT,
                parent_id       TEXT,
                node_id         TEXT,
                version         INTEGER DEFAULT 1,
                file_path       TEXT,
                embedding       BLOB,
                ext_source_url  TEXT,
                ext_source_id   TEXT,
                FOREIGN KEY (parent_id) REFERENCES memory_entries(id)
            );

            CREATE TABLE IF NOT EXISTS memory_disciplines (
                memory_id   TEXT NOT NULL,
                discipline  TEXT NOT NULL,
                PRIMARY KEY (memory_id, discipline),
                FOREIGN KEY (memory_id) REFERENCES memory_entries(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS memory_features (
                memory_id  TEXT NOT NULL,
                feature    TEXT NOT NULL,
                PRIMARY KEY (memory_id, feature),
                FOREIGN KEY (memory_id) REFERENCES memory_entries(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS memory_paths (
                memory_id    TEXT NOT NULL,
                path_pattern TEXT NOT NULL,
                PRIMARY KEY (memory_id, path_pattern),
                FOREIGN KEY (memory_id) REFERENCES memory_entries(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS memory_tags (
                memory_id TEXT NOT NULL,
                tag       TEXT NOT NULL,
                PRIMARY KEY (memory_id, tag),
                FOREIGN KEY (memory_id) REFERENCES memory_entries(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS memory_relations (
                from_id  TEXT NOT NULL,
                to_id    TEXT NOT NULL,
                relation TEXT NOT NULL,
                PRIMARY KEY (from_id, to_id, relation)
            );

            CREATE INDEX IF NOT EXISTS idx_layer ON memory_entries(layer);
            CREATE INDEX IF NOT EXISTS idx_freshness ON memory_entries(freshness);
            CREATE INDEX IF NOT EXISTS idx_parent ON memory_entries(parent_id);
            CREATE INDEX IF NOT EXISTS idx_node ON memory_entries(node_id);
            CREATE INDEX IF NOT EXISTS idx_created ON memory_entries(created_at);
            CREATE INDEX IF NOT EXISTS idx_disc ON memory_disciplines(discipline);
            CREATE INDEX IF NOT EXISTS idx_feat ON memory_features(feature);
            """;
        cmd.ExecuteNonQuery();

        using var ftsCmd = _db.CreateCommand();
        ftsCmd.CommandText = """
            CREATE VIRTUAL TABLE IF NOT EXISTS memory_fts USING fts5(
                id, summary, content, tags, tokenize='unicode61'
            );
            """;
        ftsCmd.ExecuteNonQuery();

        MigrateSchemaIfNeeded();
    }

    private void MigrateSchemaIfNeeded()
    {
        var hasContent = false;
        using (var pragma = _db!.CreateCommand())
        {
            pragma.CommandText = "PRAGMA table_info(memory_entries)";
            using var reader = pragma.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), "content", StringComparison.OrdinalIgnoreCase))
                {
                    hasContent = true;
                    break;
                }
            }
        }

        if (hasContent) return;

        _logger.LogInformation("迁移 Schema: memory_entries 表添加 content 列");
        using var alter = _db.CreateCommand();
        alter.CommandText = "ALTER TABLE memory_entries ADD COLUMN content TEXT NOT NULL DEFAULT ''";
        alter.ExecuteNonQuery();
    }

    /// <summary>如果 SQLite 为空但有旧版 JSON 文件，一次性迁移到 SQLite</summary>
    private int RebuildFromJsonIfNeeded()
    {
        using var countCmd = _db!.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*) FROM memory_entries";
        var dbCount = Convert.ToInt32(countCmd.ExecuteScalar());

        if (dbCount > 0)
            return dbCount;

        var entriesDir = Path.Combine(_memoryDir, "entries");
        var jsonFiles = Directory.Exists(entriesDir)
            ? Directory.GetFiles(entriesDir, "*.json", SearchOption.AllDirectories)
            : [];

        if (jsonFiles.Length == 0)
            return 0;

        _logger.LogInformation("SQLite 为空，从 {Count} 个 JSON 文件迁移数据...", jsonFiles.Length);

        using var transaction = _db.BeginTransaction();
        var count = 0;
        foreach (var file in jsonFiles)
        {
            try
            {
                var json = File.ReadAllText(file);
                var entry = JsonSerializer.Deserialize<MemoryEntry>(json, JsonOpts);
                if (entry != null)
                {
                    InsertIntoSqlite(entry);
                    count++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "跳过损坏的 JSON: {File}", file);
            }
        }
        transaction.Commit();

        _logger.LogInformation("从 JSON 迁移完成: {Count} 条记忆", count);
        return count;
    }

    // ═══════════════════════════════════════════
    //  JSON 导入（运维工具，用于数据迁移/恢复）
    // ═══════════════════════════════════════════

    private string EntriesDir => Path.Combine(_memoryDir, "entries");

    /// <summary>
    /// 全量导入：清空 SQLite 所有表，从 entries/*.json 文件导入全部数据。
    /// 适用于：从旧版本迁移、从备份恢复。
    /// </summary>
    public (int imported, int skipped) RebuildIndex(bool rewriteJson = false)
    {
        if (_db == null) throw new InvalidOperationException("MemoryStore not initialized");

        ClearAllSqliteTables();

        var jsonFiles = Directory.Exists(EntriesDir)
            ? Directory.GetFiles(EntriesDir, "*.json", SearchOption.AllDirectories)
            : [];

        if (jsonFiles.Length == 0)
            return (0, 0);

        _logger.LogInformation("全量导入: 扫描到 {Count} 个 JSON 文件", jsonFiles.Length);

        var imported = 0;
        var skipped = 0;
        using var transaction = _db.BeginTransaction();
        foreach (var file in jsonFiles)
        {
            try
            {
                var json = File.ReadAllText(file);
                var entry = JsonSerializer.Deserialize<MemoryEntry>(json, JsonOpts);
                if (entry != null)
                {
                    InsertIntoSqlite(entry);
                    imported++;
                }
                else skipped++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "跳过损坏的 JSON: {File}", file);
                skipped++;
            }
        }
        transaction.Commit();

        _logger.LogInformation("全量导入完成: 导入 {Imported}, 跳过 {Skipped}", imported, skipped);
        return (imported, skipped);
    }

    /// <summary>
    /// 增量导入：将 entries/*.json 中新增的文件补入 SQLite。
    /// 适用于：从外部导入少量 JSON 文件。
    /// </summary>
    public (int added, int removed, int skipped) SyncFromJson()
    {
        if (_db == null) throw new InvalidOperationException("MemoryStore not initialized");

        var jsonFiles = Directory.Exists(EntriesDir)
            ? Directory.GetFiles(EntriesDir, "*.json", SearchOption.AllDirectories)
            : [];

        var existingIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var cmd = _db.CreateCommand())
        {
            cmd.CommandText = "SELECT id FROM memory_entries";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                existingIds.Add(reader.GetString(0));
        }

        var jsonIdToFile = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var skipped = 0;
        foreach (var file in jsonFiles)
        {
            try
            {
                var json = File.ReadAllText(file);
                var entry = JsonSerializer.Deserialize<MemoryEntry>(json, JsonOpts);
                if (entry != null)
                    jsonIdToFile[entry.Id] = file;
                else
                    skipped++;
            }
            catch
            {
                skipped++;
            }
        }

        var toAdd = jsonIdToFile.Keys.Where(id => !existingIds.Contains(id)).ToList();
        var toRemove = existingIds.Where(id => !jsonIdToFile.ContainsKey(id)).ToList();

        _logger.LogInformation("增量导入: 新增 {Add}, 跳过 {Skip}",
            toAdd.Count, skipped);

        using var transaction = _db.BeginTransaction();

        foreach (var id in toAdd)
        {
            try
            {
                var file = jsonIdToFile[id];
                var json = File.ReadAllText(file);
                var entry = JsonSerializer.Deserialize<MemoryEntry>(json, JsonOpts);
                if (entry != null)
                    InsertIntoSqlite(entry);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "增量导入跳过: {Id}", id);
                skipped++;
            }
        }

        transaction.Commit();

        _logger.LogInformation("增量导入完成: 新增 {Added}", toAdd.Count);
        return (toAdd.Count, 0, skipped);
    }

    /// <summary>
    /// 从 SQLite 导出为 JSON 文件：将全部记忆导出到 entries/*.json。
    /// 适用于：数据备份、导出到其他系统。
    /// </summary>
    public (int exported, int skipped) ExportToJson()
    {
        if (_db == null) throw new InvalidOperationException("MemoryStore not initialized");

        using var cmd = _db.CreateCommand();
        cmd.CommandText = SelectAllColumns;

        var entries = ReadEntriesFromCommand(cmd);

        if (entries.Count == 0)
        {
            _logger.LogInformation("ExportToJson: DB 为空，无记忆可导出");
            return (0, 0);
        }

        _logger.LogInformation("ExportToJson: 从 DB 导出 {Count} 条记忆到 JSON", entries.Count);

        var entriesDir = EntriesDir;
        var exported = 0;
        var skipped = 0;
        foreach (var entry in entries)
        {
            try
            {
                var yearMonth = entry.CreatedAt.ToString("yyyy-MM");
                var dir = Path.Combine(entriesDir, yearMonth);
                Directory.CreateDirectory(dir);
                var filePath = Path.Combine(dir, $"{entry.Id}.json");
                var json = JsonSerializer.Serialize(entry, JsonOpts);
                File.WriteAllText(filePath, json);
                exported++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ExportToJson 跳过: {Id}", entry.Id);
                skipped++;
            }
        }

        _logger.LogInformation("ExportToJson 完成: 导出 {Exported}, 跳过 {Skipped}", exported, skipped);
        return (exported, skipped);
    }

    private List<string> QueryMultiValues(string table, string column, string memoryId)
    {
        var actualTable = table;
        var idColumn = "memory_id";
        if (table == "memory_relations_export")
        {
            actualTable = "memory_relations";
            idColumn = "from_id";
        }

        using var cmd = _db!.CreateCommand();
        cmd.CommandText = $"SELECT {column} FROM {actualTable} WHERE {idColumn} = @id";
        cmd.Parameters.AddWithValue("@id", memoryId);

        var list = new List<string>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) list.Add(reader.GetString(0));
        return list;
    }

    private void ClearAllSqliteTables()
    {
        var tables = new[] { "memory_fts", "memory_relations", "memory_tags", "memory_paths", "memory_features", "memory_disciplines", "memory_entries" };
        foreach (var table in tables)
        {
            using var cmd = _db!.CreateCommand();
            cmd.CommandText = $"DELETE FROM {table}";
            cmd.ExecuteNonQuery();
        }
    }

    // ═══════════════════════════════════════════
    //  写入
    // ═══════════════════════════════════════════

    /// <summary>写入一条记忆到 SQLite</summary>
    public MemoryEntry Insert(MemoryEntry entry)
    {
        InsertIntoSqlite(entry);
        return entry;
    }

    /// <summary>更新一条记忆</summary>
    public MemoryEntry Update(MemoryEntry entry)
    {
        DeleteFromSqlite(entry.Id);
        InsertIntoSqlite(entry);
        return entry;
    }

    /// <summary>删除一条记忆</summary>
    public bool Delete(string id)
    {
        using var check = _db!.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM memory_entries WHERE id = @id";
        check.Parameters.AddWithValue("@id", id);
        if (Convert.ToInt32(check.ExecuteScalar()) == 0) return false;

        DeleteFromSqlite(id);
        return true;
    }

    /// <summary>批量写入（事务）</summary>
    public List<MemoryEntry> InsertBatch(List<MemoryEntry> entries)
    {
        using var transaction = _db!.BeginTransaction();
        foreach (var entry in entries)
            InsertIntoSqlite(entry);
        transaction.Commit();
        return entries;
    }

    /// <summary>更新鲜活度状态</summary>
    public void UpdateFreshness(string id, FreshnessStatus freshness)
    {
        using var cmd = _db!.CreateCommand();
        cmd.CommandText = "UPDATE memory_entries SET freshness = @freshness WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@freshness", freshness.ToString());
        cmd.ExecuteNonQuery();
    }

    public void UpdateTags(string id, List<string> tags)
    {
        using var cmd = _db!.CreateCommand();
        cmd.CommandText = "UPDATE memory_entries SET tags = @tags WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@tags", string.Join(",", tags));
        cmd.ExecuteNonQuery();
    }

    /// <summary>扫描并自动降级过期的记忆</summary>
    public int DecayStaleMemories()
    {
        var now = DateTime.UtcNow.ToString("O");
        
        // 找出所有 Fresh 且已过期的记忆
        using var selectCmd = _db!.CreateCommand();
        selectCmd.CommandText = """
            SELECT id FROM memory_entries 
            WHERE freshness = 'Fresh' 
              AND stale_after IS NOT NULL 
              AND stale_after < @now
            """;
        selectCmd.Parameters.AddWithValue("@now", now);
        
        var idsToDecay = new List<string>();
        using (var reader = selectCmd.ExecuteReader())
        {
            while (reader.Read())
            {
                idsToDecay.Add(reader.GetString(0));
            }
        }

        if (idsToDecay.Count == 0) return 0;

        using var transaction = _db.BeginTransaction();
        try
        {
            using var updateCmd = _db.CreateCommand();
            updateCmd.CommandText = """
                UPDATE memory_entries 
                SET freshness = 'Aging' 
                WHERE id = @id
                """;
            var idParam = updateCmd.Parameters.Add("@id", SqliteType.Text);

            foreach (var id in idsToDecay)
            {
                idParam.Value = id;
                updateCmd.ExecuteNonQuery();
            }
            
            transaction.Commit();
            return idsToDecay.Count;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    /// <summary>标记验证时间</summary>
    public void Verify(string id)
    {
        var now = DateTime.UtcNow;
        using var cmd = _db!.CreateCommand();
        cmd.CommandText = """
            UPDATE memory_entries
            SET freshness = 'Fresh', last_verified_at = @now
            WHERE id = @id
            """;
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@now", now.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    /// <summary>更新向量</summary>
    public void UpdateEmbedding(string id, float[] embedding)
    {
        using var cmd = _db!.CreateCommand();
        cmd.CommandText = "UPDATE memory_entries SET embedding = @emb WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@emb", FloatsToBytes(embedding));
        cmd.ExecuteNonQuery();
    }

    // ═══════════════════════════════════════════
    //  读取
    // ═══════════════════════════════════════════

    /// <summary>按 ID 获取完整记忆</summary>
    public MemoryEntry? GetById(string id)
    {
        using var cmd = _db!.CreateCommand();
        cmd.CommandText = SelectAllColumns + " WHERE e.id = @id";
        cmd.Parameters.AddWithValue("@id", id);

        using var reader = cmd.ExecuteReader();
        return reader.Read() ? ReadEntryFromRow(reader) : null;
    }

    /// <summary>结构化查询</summary>
    public List<MemoryEntry> Query(MemoryFilter filter)
    {
        var (whereClause, parameters) = BuildWhereClause(filter);
        var sql = $"""
            SELECT DISTINCT e.id, e.type, e.layer, e.source, e.content, e.summary,
                   e.importance, e.freshness, e.created_at, e.last_verified_at,
                   e.stale_after, e.superseded_by, e.parent_id, e.node_id,
                   e.version, e.ext_source_url, e.ext_source_id
            FROM memory_entries e
            LEFT JOIN memory_disciplines d ON e.id = d.memory_id
            LEFT JOIN memory_features f ON e.id = f.memory_id
            LEFT JOIN memory_tags t ON e.id = t.memory_id
            {whereClause}
            ORDER BY e.importance DESC, e.created_at DESC
            LIMIT @limit OFFSET @offset
            """;

        using var cmd = _db!.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
        cmd.Parameters.AddWithValue("@limit", filter.Limit);
        cmd.Parameters.AddWithValue("@offset", filter.Offset);

        return ReadEntriesFromCommand(cmd);
    }

    /// <summary>FTS5 全文搜索</summary>
    public List<(string Id, double Rank)> FullTextSearch(string query, int limit = 20)
    {
        var results = new List<(string Id, double Rank)>();

        using var cmd = _db!.CreateCommand();
        cmd.CommandText = """
            SELECT id, rank FROM memory_fts
            WHERE memory_fts MATCH @query
            ORDER BY rank
            LIMIT @limit
            """;
        cmd.Parameters.AddWithValue("@query", EscapeFtsQuery(query));
        cmd.Parameters.AddWithValue("@limit", limit);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add((reader.GetString(0), reader.GetDouble(1)));
        }
        return results;
    }

    /// <summary>获取所有带向量的记忆 ID + 向量（用于内存向量索引构建）</summary>
    public List<(string Id, float[] Embedding)> GetAllEmbeddings()
    {
        var results = new List<(string, float[])>();

        using var cmd = _db!.CreateCommand();
        cmd.CommandText = "SELECT id, embedding FROM memory_entries WHERE embedding IS NOT NULL";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var id = reader.GetString(0);
            var bytes = (byte[])reader.GetValue(1);
            results.Add((id, BytesToFloats(bytes)));
        }
        return results;
    }

    /// <summary>按约束链向上查找</summary>
    public List<MemoryEntry> GetConstraintChain(string memoryId)
    {
        var chain = new List<MemoryEntry>();
        var visited = new HashSet<string>();
        var currentId = memoryId;

        while (currentId != null && visited.Add(currentId))
        {
            var entry = GetById(currentId);
            if (entry == null) break;
            chain.Add(entry);
            currentId = entry.ParentId;
        }

        chain.Reverse();
        return chain;
    }

    /// <summary>统计信息</summary>
    public MemoryStats GetStats()
    {
        int total;
        int conflictCount;
        using (var cmd = _db!.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM memory_entries";
            total = Convert.ToInt32(cmd.ExecuteScalar());
        }
        using (var cmd = _db!.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM memory_tags WHERE tag = '#conflict'";
            conflictCount = Convert.ToInt32(cmd.ExecuteScalar());
        }

        return new MemoryStats
        {
            Total = total,
            ConflictCount = conflictCount,
            ByLayer = CountGroupBy("memory_entries", "layer"),
            ByDiscipline = CountGroupBy("memory_disciplines", "discipline"),
            ByFeature = CountGroupBy("memory_features", "feature"),
            ByFreshness = CountGroupBy("memory_entries", "freshness"),
            ByType = CountGroupBy("memory_entries", "type")
        };
    }

    public int Count()
    {
        using var cmd = _db!.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM memory_entries";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    // ═══════════════════════════════════════════
    //  内部：SQLite 操作
    // ═══════════════════════════════════════════

    private void InsertIntoSqlite(MemoryEntry entry)
    {
        using var cmd = _db!.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO memory_entries
                (id, type, layer, source, content, summary, importance, freshness,
                 created_at, last_verified_at, stale_after, superseded_by,
                 parent_id, node_id, version, embedding, ext_source_url, ext_source_id)
            VALUES
                (@id, @type, @layer, @source, @content, @summary, @importance, @freshness,
                 @created_at, @last_verified_at, @stale_after, @superseded_by,
                 @parent_id, @node_id, @version, @embedding, @ext_source_url, @ext_source_id)
            """;

        cmd.Parameters.AddWithValue("@id", entry.Id);
        cmd.Parameters.AddWithValue("@type", entry.Type.ToString());
        cmd.Parameters.AddWithValue("@layer", entry.Layer.ToString());
        cmd.Parameters.AddWithValue("@source", entry.Source.ToString());
        cmd.Parameters.AddWithValue("@content", entry.Content);
        cmd.Parameters.AddWithValue("@summary", (object?)entry.Summary ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@importance", entry.Importance);
        cmd.Parameters.AddWithValue("@freshness", entry.Freshness.ToString());
        cmd.Parameters.AddWithValue("@created_at", entry.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@last_verified_at",
            entry.LastVerifiedAt.HasValue ? entry.LastVerifiedAt.Value.ToString("O") : DBNull.Value);
        cmd.Parameters.AddWithValue("@stale_after",
            entry.StaleAfter.HasValue ? entry.StaleAfter.Value.ToString("O") : DBNull.Value);
        cmd.Parameters.AddWithValue("@superseded_by", (object?)entry.SupersededBy ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@parent_id", (object?)entry.ParentId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@node_id", (object?)entry.NodeId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@version", entry.Version);
        cmd.Parameters.AddWithValue("@embedding",
            entry.Embedding != null ? FloatsToBytes(entry.Embedding) : DBNull.Value);
        cmd.Parameters.AddWithValue("@ext_source_url", (object?)entry.ExternalSourceUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ext_source_id", (object?)entry.ExternalSourceId ?? DBNull.Value);
        cmd.ExecuteNonQuery();

        InsertMultiValues("memory_disciplines", "discipline", entry.Id, entry.Disciplines);
        InsertMultiValues("memory_features", "feature", entry.Id, entry.Features);
        InsertMultiValues("memory_paths", "path_pattern", entry.Id, entry.PathPatterns);
        InsertMultiValues("memory_tags", "tag", entry.Id, entry.Tags);

        if (entry.RelatedIds.Count > 0)
        {
            using var relCmd = _db.CreateCommand();
            relCmd.CommandText = "INSERT OR IGNORE INTO memory_relations (from_id, to_id, relation) VALUES (@from, @to, 'related')";
            var fromParam = relCmd.Parameters.AddWithValue("@from", entry.Id);
            var toParam = relCmd.Parameters.Add("@to", SqliteType.Text);

            foreach (var relatedId in entry.RelatedIds)
            {
                toParam.Value = relatedId;
                relCmd.ExecuteNonQuery();
            }
        }

        InsertFts(entry);
    }

    private void InsertMultiValues(string table, string column, string memoryId, List<string> values)
    {
        if (values.Count == 0) return;

        using var cmd = _db!.CreateCommand();
        cmd.CommandText = $"INSERT OR IGNORE INTO {table} (memory_id, {column}) VALUES (@mid, @val)";
        cmd.Parameters.AddWithValue("@mid", memoryId);
        var valParam = cmd.Parameters.Add("@val", SqliteType.Text);

        foreach (var value in values)
        {
            valParam.Value = value;
            cmd.ExecuteNonQuery();
        }
    }

    private void InsertFts(MemoryEntry entry)
    {
        using var cmd = _db!.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO memory_fts (id, summary, content, tags)
            VALUES (@id, @summary, @content, @tags)
            """;
        cmd.Parameters.AddWithValue("@id", entry.Id);
        cmd.Parameters.AddWithValue("@summary", entry.Summary ?? string.Empty);
        cmd.Parameters.AddWithValue("@content", entry.Content);
        cmd.Parameters.AddWithValue("@tags", string.Join(" ", entry.Tags));
        cmd.ExecuteNonQuery();
    }

    private void DeleteFromSqlite(string id)
    {
        var tables = new[] { "memory_fts", "memory_disciplines", "memory_features", "memory_paths", "memory_tags" };
        foreach (var table in tables)
        {
            using var cmd = _db!.CreateCommand();
            var idColumn = table == "memory_fts" ? "id" : "memory_id";
            cmd.CommandText = $"DELETE FROM {table} WHERE {idColumn} = @id";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        using var delRelCmd = _db!.CreateCommand();
        delRelCmd.CommandText = "DELETE FROM memory_relations WHERE from_id = @id OR to_id = @id";
        delRelCmd.Parameters.AddWithValue("@id", id);
        delRelCmd.ExecuteNonQuery();

        using var delCmd = _db!.CreateCommand();
        delCmd.CommandText = "DELETE FROM memory_entries WHERE id = @id";
        delCmd.Parameters.AddWithValue("@id", id);
        delCmd.ExecuteNonQuery();
    }

    // ═══════════════════════════════════════════
    //  内部：查询构建
    // ═══════════════════════════════════════════

    private (string Sql, List<(string Name, object Value)> Params) BuildWhereClause(MemoryFilter filter)
    {
        var conditions = new List<string>();
        var parameters = new List<(string, object)>();

        if (filter.Layers is { Count: > 0 })
        {
            var placeholders = filter.Layers.Select((l, i) => $"@layer{i}").ToList();
            conditions.Add($"e.layer IN ({string.Join(",", placeholders)})");
            for (var i = 0; i < filter.Layers.Count; i++)
                parameters.Add(($"@layer{i}", filter.Layers[i].ToString()));
        }

        if (filter.Types is { Count: > 0 })
        {
            var placeholders = filter.Types.Select((t, i) => $"@type{i}").ToList();
            conditions.Add($"e.type IN ({string.Join(",", placeholders)})");
            for (var i = 0; i < filter.Types.Count; i++)
                parameters.Add(($"@type{i}", filter.Types[i].ToString()));
        }

        if (filter.Disciplines is { Count: > 0 })
        {
            var placeholders = filter.Disciplines.Select((_, i) => $"@disc{i}").ToList();
            conditions.Add($"d.discipline IN ({string.Join(",", placeholders)})");
            for (var i = 0; i < filter.Disciplines.Count; i++)
                parameters.Add(($"@disc{i}", filter.Disciplines[i]));
        }

        if (filter.Features is { Count: > 0 })
        {
            var placeholders = filter.Features.Select((_, i) => $"@feat{i}").ToList();
            conditions.Add($"f.feature IN ({string.Join(",", placeholders)})");
            for (var i = 0; i < filter.Features.Count; i++)
                parameters.Add(($"@feat{i}", filter.Features[i]));
        }

        if (!string.IsNullOrEmpty(filter.NodeId))
        {
            conditions.Add("e.node_id = @nodeId");
            parameters.Add(("@nodeId", filter.NodeId));
        }

        if (filter.Tags is { Count: > 0 })
        {
            var placeholders = filter.Tags.Select((_, i) => $"@tag{i}").ToList();
            conditions.Add($"t.tag IN ({string.Join(",", placeholders)})");
            for (var i = 0; i < filter.Tags.Count; i++)
                parameters.Add(($"@tag{i}", filter.Tags[i]));
        }

        AddFreshnessCondition(conditions, parameters, filter.Freshness);

        var where = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";
        return (where, parameters);
    }

    private static void AddFreshnessCondition(
        List<string> conditions, List<(string, object)> parameters, FreshnessFilter filter)
    {
        switch (filter)
        {
            case FreshnessFilter.FreshOnly:
                conditions.Add("e.freshness = @fresh");
                parameters.Add(("@fresh", "Fresh"));
                break;
            case FreshnessFilter.FreshAndAging:
                conditions.Add("e.freshness IN ('Fresh', 'Aging')");
                break;
            case FreshnessFilter.IncludeStale:
                conditions.Add("e.freshness IN ('Fresh', 'Aging', 'Stale')");
                break;
            case FreshnessFilter.IncludeArchived:
                conditions.Add("e.freshness IN ('Fresh', 'Aging', 'Stale', 'Archived')");
                break;
            case FreshnessFilter.All:
                break;
        }
    }

    private const string SelectAllColumns = """
        SELECT e.id, e.type, e.layer, e.source, e.content, e.summary,
               e.importance, e.freshness, e.created_at, e.last_verified_at,
               e.stale_after, e.superseded_by, e.parent_id, e.node_id,
               e.version, e.ext_source_url, e.ext_source_id
        FROM memory_entries e
        """;

    private List<MemoryEntry> ReadEntriesFromCommand(SqliteCommand cmd)
    {
        var entries = new List<MemoryEntry>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            try
            {
                entries.Add(ReadEntryFromRow(reader));
            }
            catch { /* skip corrupted rows */ }
        }
        return entries;
    }

    private MemoryEntry ReadEntryFromRow(SqliteDataReader reader)
    {
        var id = reader.GetString(0);
        Enum.TryParse<MemoryType>(reader.GetString(1), true, out var memType);
        Enum.TryParse<KnowledgeLayer>(reader.GetString(2), true, out var layer);
        Enum.TryParse<MemorySource>(reader.GetString(3), true, out var source);
        Enum.TryParse<FreshnessStatus>(reader.GetString(7), true, out var freshness);

        return new MemoryEntry
        {
            Id = id,
            Type = memType,
            Layer = layer,
            Source = source,
            Content = reader.IsDBNull(4) ? "" : reader.GetString(4),
            Summary = reader.IsDBNull(5) ? null : reader.GetString(5),
            Importance = reader.GetDouble(6),
            Freshness = freshness,
            CreatedAt = DateTime.TryParse(reader.GetString(8), out var ca) ? ca : DateTime.UtcNow,
            LastVerifiedAt = reader.IsDBNull(9) ? null : DateTime.TryParse(reader.GetString(9), out var lv) ? lv : null,
            StaleAfter = reader.IsDBNull(10) ? null : DateTime.TryParse(reader.GetString(10), out var sa) ? sa : null,
            SupersededBy = reader.IsDBNull(11) ? null : reader.GetString(11),
            ParentId = reader.IsDBNull(12) ? null : reader.GetString(12),
            NodeId = reader.IsDBNull(13) ? null : reader.GetString(13),
            Version = reader.GetInt32(14),
            ExternalSourceUrl = reader.IsDBNull(15) ? null : reader.GetString(15),
            ExternalSourceId = reader.IsDBNull(16) ? null : reader.GetString(16),
            Disciplines = QueryMultiValues("memory_disciplines", "discipline", id),
            Features = QueryMultiValues("memory_features", "feature", id),
            Tags = QueryMultiValues("memory_tags", "tag", id),
            PathPatterns = QueryMultiValues("memory_paths", "path_pattern", id),
            RelatedIds = QueryMultiValues("memory_relations_export", "to_id", id),
        };
    }

    private Dictionary<string, int> CountGroupBy(string table, string column)
    {
        var result = new Dictionary<string, int>();
        using var cmd = _db!.CreateCommand();
        cmd.CommandText = $"SELECT {column}, COUNT(*) FROM {table} GROUP BY {column}";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            result[reader.GetString(0)] = reader.GetInt32(1);
        return result;
    }

    private static string EscapeFtsQuery(string query)
    {
        return string.Join(" OR ",
            query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(word => $"\"{word}\""));
    }

    // ═══════════════════════════════════════════
    //  内部：向量序列化
    // ═══════════════════════════════════════════

    private static byte[] FloatsToBytes(float[] floats)
    {
        var bytes = new byte[floats.Length * sizeof(float)];
        Buffer.BlockCopy(floats, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static float[] BytesToFloats(byte[] bytes)
    {
        var floats = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, floats, 0, bytes.Length);
        return floats;
    }

    public void Dispose()
    {
        _db?.Dispose();
        _db = null;
        GC.SuppressFinalize(this);
    }
}
