using Dna.Knowledge;
using Dna.Knowledge.FileProtocol;
using Dna.Memory.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dna.Memory.Store;

/// <summary>
/// 记忆存储 — 纯 SQLite 存储引擎。
/// memory.db 存储索引/元数据/向量/FTS，记忆内容不再落盘为 JSON 文件。
/// </summary>
public partial class MemoryStore : IDisposable
{
    private readonly ILogger<MemoryStore> _logger;
    private SqliteConnection? _db;
    private string _storePath = string.Empty;
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
        _logger.LogInformation("MemoryStore 已重载");
    }

    public void Initialize(string storePath)
    {
        if (_initialized && _storePath == storePath) return;

        _storePath = storePath;
        _memoryDir = storePath;
        Directory.CreateDirectory(_memoryDir);

        var dbPath = Path.Combine(_memoryDir, "memory.db");
        var legacyRootIndexDbPath = Path.Combine(_memoryDir, "index.db");
        var legacyNestedMemoryDirIndexDbPath = Path.Combine(_memoryDir, "memory", "index.db");

        if (!File.Exists(dbPath))
        {
            if (File.Exists(legacyRootIndexDbPath))
            {
                MoveSqliteDbWithSidecars(legacyRootIndexDbPath, dbPath);
                _logger.LogInformation("已迁移旧记忆库: {Old} -> {New}", legacyRootIndexDbPath, dbPath);
            }
            else if (File.Exists(legacyNestedMemoryDirIndexDbPath))
            {
                MoveSqliteDbWithSidecars(legacyNestedMemoryDirIndexDbPath, dbPath);
                _logger.LogInformation("已迁移旧记忆库: {Old} -> {New}", legacyNestedMemoryDirIndexDbPath, dbPath);
            }
        }

        _db?.Dispose();
        _db = new SqliteConnection($"Data Source={dbPath}");
        _db.Open();

        using (var pragma = _db.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;";
            pragma.ExecuteNonQuery();
        }

        EnsureSchema();

        _initialized = true;

        // 文件协议 seed：memory.db 为空时从 .agentic-os/memory/ 加载明文文件
        if (Count() == 0)
            TrySeedFromFileProtocol();

        _logger.LogInformation("MemoryStore 已初始化: db={DbPath}, store={Store}, entries={Count}",
            dbPath, storePath, Count());
    }

    private static void MoveSqliteDbWithSidecars(string fromDbPath, string toDbPath)
    {
        File.Move(fromDbPath, toDbPath, overwrite: true);

        var fromWal = fromDbPath + "-wal";
        var fromShm = fromDbPath + "-shm";
        var toWal = toDbPath + "-wal";
        var toShm = toDbPath + "-shm";

        if (File.Exists(fromWal))
            File.Move(fromWal, toWal, overwrite: true);
        if (File.Exists(fromShm))
            File.Move(fromShm, toShm, overwrite: true);
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
                stage           TEXT NOT NULL DEFAULT 'LongTerm',
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
            CREATE INDEX IF NOT EXISTS idx_stage ON memory_entries(stage);
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
        MigrateLegacyLayerValuesToNodeType();
    }

    private void MigrateSchemaIfNeeded()
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var pragma = _db!.CreateCommand())
        {
            pragma.CommandText = "PRAGMA table_info(memory_entries)";
            using var reader = pragma.ExecuteReader();
            while (reader.Read())
                columns.Add(reader.GetString(1));
        }

        if (!columns.Contains("content"))
        {
            _logger.LogInformation("迁移 Schema: memory_entries 表添加 content 列");
            using var alterContent = _db.CreateCommand();
            alterContent.CommandText = "ALTER TABLE memory_entries ADD COLUMN content TEXT NOT NULL DEFAULT ''";
            alterContent.ExecuteNonQuery();
        }

        if (!columns.Contains("stage"))
        {
            _logger.LogInformation("迁移 Schema: memory_entries 表添加 stage 列");
            using var alterStage = _db.CreateCommand();
            alterStage.CommandText = "ALTER TABLE memory_entries ADD COLUMN stage TEXT NOT NULL DEFAULT 'LongTerm'";
            alterStage.ExecuteNonQuery();
        }
    }

    private void MigrateLegacyLayerValuesToNodeType()
    {
        using var cmd = _db!.CreateCommand();
        cmd.CommandText = """
            UPDATE memory_entries
            SET layer = CASE LOWER(layer)
                WHEN 'projectvision' THEN 'Project'
                WHEN 'disciplinestandard' THEN 'Department'
                WHEN 'crossdiscipline' THEN 'Team'
                WHEN 'featuresystem' THEN 'Technical'
                WHEN 'implementation' THEN 'Team'
                WHEN 'root' THEN 'Project'
                WHEN 'module' THEN 'Technical'
                WHEN 'group' THEN 'Technical'
                WHEN 'crosswork' THEN 'Team'
                ELSE layer
            END
            WHERE LOWER(layer) IN (
                'projectvision','disciplinestandard','crossdiscipline','featuresystem','implementation',
                'root','module','crosswork'
            )
            """;
        var migrated = cmd.ExecuteNonQuery();
        if (migrated > 0)
            _logger.LogInformation("记忆层级字段已迁移到 NodeType 语义，共 {Count} 条", migrated);
    }

    // ═══════════════════════════════════════════
    //  索引维护（纯 DB）
    // ═══════════════════════════════════════════

    /// <summary>
    /// 重建搜索索引：重建 FTS 文本索引（不触碰业务数据）。
    /// </summary>
    public (int imported, int skipped) RebuildIndex(bool rewriteJson = false)
    {
        if (_db == null) throw new InvalidOperationException("MemoryStore not initialized");

        using var transaction = _db.BeginTransaction();
        using var clearFts = _db.CreateCommand();
        clearFts.Transaction = transaction;
        clearFts.CommandText = "DELETE FROM memory_fts";
        clearFts.ExecuteNonQuery();

        using var insertFts = _db.CreateCommand();
        insertFts.Transaction = transaction;
        insertFts.CommandText = """
            INSERT INTO memory_fts (id, summary, content, tags)
            SELECT e.id,
                   COALESCE(e.summary, ''),
                   COALESCE(e.content, ''),
                   COALESCE((SELECT GROUP_CONCAT(t.tag, ' ')
                             FROM memory_tags t
                             WHERE t.memory_id = e.id), '')
            FROM memory_entries e
            """;
        var indexed = insertFts.ExecuteNonQuery();

        transaction.Commit();
        _logger.LogInformation("已重建 memory_fts 索引：{Count} 条", indexed);
        return (indexed, 0);
    }

    /// <summary>
    /// 兼容旧接口：JSON 增量同步已废弃，改为重建搜索索引。
    /// </summary>
    public (int added, int removed, int skipped) SyncFromJson()
    {
        var (indexed, _) = RebuildIndex();
        return (indexed, 0, 0);
    }

    /// <summary>
    /// 兼容旧接口：JSON 导出已废弃（当前为纯 DB 存储）。
    /// </summary>
    public (int exported, int skipped) ExportToJson()
    {
        var total = Count();
        _logger.LogInformation("ExportToJson 已废弃：当前为纯 DB 存储，entries={Count}", total);
        return (0, total);
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
        using var transaction = _db!.BeginTransaction();

        using (var deleteCmd = _db.CreateCommand())
        {
            deleteCmd.Transaction = transaction;
            deleteCmd.CommandText = "DELETE FROM memory_tags WHERE memory_id = @id";
            deleteCmd.Parameters.AddWithValue("@id", id);
            deleteCmd.ExecuteNonQuery();
        }

        if (tags.Count > 0)
        {
            using var insertCmd = _db.CreateCommand();
            insertCmd.Transaction = transaction;
            insertCmd.CommandText = "INSERT OR IGNORE INTO memory_tags (memory_id, tag) VALUES (@mid, @tag)";
            insertCmd.Parameters.AddWithValue("@mid", id);
            var tagParam = insertCmd.Parameters.Add("@tag", SqliteType.Text);

            foreach (var tag in tags)
            {
                tagParam.Value = tag;
                insertCmd.ExecuteNonQuery();
            }
        }

        using (var updateFts = _db.CreateCommand())
        {
            updateFts.Transaction = transaction;
            updateFts.CommandText = """
                UPDATE memory_fts
                SET tags = @tags
                WHERE id = @id
                """;
            updateFts.Parameters.AddWithValue("@id", id);
            updateFts.Parameters.AddWithValue("@tags", string.Join(" ", tags));
            updateFts.ExecuteNonQuery();
        }

        transaction.Commit();
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
                   e.importance, e.freshness, e.stage, e.created_at, e.last_verified_at,
                   e.stale_after, e.superseded_by, e.parent_id, e.node_id,
                   e.version, e.ext_source_url, e.ext_source_id
            FROM memory_entries e
            LEFT JOIN memory_disciplines d ON e.id = d.memory_id
            LEFT JOIN memory_features f ON e.id = f.memory_id
            LEFT JOIN memory_tags t ON e.id = t.memory_id
            {whereClause}
            ORDER BY e.created_at DESC, e.importance DESC
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
            ByNodeType = CountGroupBy("memory_entries", "layer"),
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
                 stage, created_at, last_verified_at, stale_after, superseded_by,
                 parent_id, node_id, version, embedding, ext_source_url, ext_source_id)
            VALUES
                (@id, @type, @layer, @source, @content, @summary, @importance, @freshness,
                 @stage, @created_at, @last_verified_at, @stale_after, @superseded_by,
                 @parent_id, @node_id, @version, @embedding, @ext_source_url, @ext_source_id)
            """;

        cmd.Parameters.AddWithValue("@id", entry.Id);
        cmd.Parameters.AddWithValue("@type", entry.Type.ToString());
        cmd.Parameters.AddWithValue("@layer", entry.NodeType.ToString());
        cmd.Parameters.AddWithValue("@source", entry.Source.ToString());
        cmd.Parameters.AddWithValue("@content", entry.Content);
        cmd.Parameters.AddWithValue("@summary", (object?)entry.Summary ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@importance", entry.Importance);
        cmd.Parameters.AddWithValue("@freshness", entry.Freshness.ToString());
        cmd.Parameters.AddWithValue("@stage", entry.Stage.ToString());
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

        var nodeTypes = filter.ResolvedNodeTypes;
        if (nodeTypes is { Count: > 0 })
        {
            var placeholders = nodeTypes.Select((l, i) => $"@layer{i}").ToList();
            conditions.Add($"e.layer IN ({string.Join(",", placeholders)})");
            for (var i = 0; i < nodeTypes.Count; i++)
                parameters.Add(($"@layer{i}", nodeTypes[i].ToString()));
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
            var nodeCandidates = TopoGraphStore.ResolveNodeIdCandidates(filter.NodeId, strict: false);
            if (nodeCandidates.Count == 1)
            {
                conditions.Add("e.node_id = @nodeId0");
                parameters.Add(("@nodeId0", nodeCandidates[0]));
            }
            else if (nodeCandidates.Count > 1)
            {
                var placeholders = nodeCandidates.Select((_, i) => $"@nodeId{i}").ToList();
                conditions.Add($"e.node_id IN ({string.Join(",", placeholders)})");
                for (var i = 0; i < nodeCandidates.Count; i++)
                    parameters.Add(($"@nodeId{i}", nodeCandidates[i]));
            }
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
               e.importance, e.freshness, e.stage, e.created_at, e.last_verified_at,
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
        var nodeType = NodeTypeCompat.TryParse(reader.GetString(2), out var parsedNodeType)
            ? parsedNodeType
            : NodeType.Technical;
        Enum.TryParse<MemorySource>(reader.GetString(3), true, out var source);
        Enum.TryParse<FreshnessStatus>(reader.GetString(7), true, out var freshness);
        var hasStage = Enum.TryParse<MemoryStage>(reader.GetString(8), true, out var stage);

        return new MemoryEntry
        {
            Id = id,
            Type = memType,
            NodeType = nodeType,
            Source = source,
            Content = reader.IsDBNull(4) ? "" : reader.GetString(4),
            Summary = reader.IsDBNull(5) ? null : reader.GetString(5),
            Importance = reader.GetDouble(6),
            Freshness = freshness,
            Stage = hasStage ? stage : MemoryStage.LongTerm,
            CreatedAt = DateTime.TryParse(reader.GetString(9), out var ca) ? ca : DateTime.UtcNow,
            LastVerifiedAt = reader.IsDBNull(10) ? null : DateTime.TryParse(reader.GetString(10), out var lv) ? lv : null,
            StaleAfter = reader.IsDBNull(11) ? null : DateTime.TryParse(reader.GetString(11), out var sa) ? sa : null,
            SupersededBy = reader.IsDBNull(12) ? null : reader.GetString(12),
            ParentId = reader.IsDBNull(13) ? null : reader.GetString(13),
            NodeId = reader.IsDBNull(14) ? null : reader.GetString(14),
            Version = reader.GetInt32(15),
            ExternalSourceUrl = reader.IsDBNull(16) ? null : reader.GetString(16),
            ExternalSourceId = reader.IsDBNull(17) ? null : reader.GetString(17),
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

    /// <summary>
    /// 从 .agentic-os/memory/ 明文文件 seed 记忆到空的 memory.db。
    /// </summary>
    private void TrySeedFromFileProtocol()
    {
        try
        {
            // _storePath 通常指向 .agentic-os/memory/ 或 .agentic-os/
            var agenticOsPath = ResolveAgenticOsPathForMemory(_storePath);
            if (agenticOsPath == null)
                return;

            var memoryFileStore = new MemoryFileStore();
            var files = memoryFileStore.LoadMemories(agenticOsPath);
            if (files.Count == 0)
                return;

            var seeded = 0;
            foreach (var file in files)
            {
                var entry = FileToMemoryEntry(file);
                if (entry == null) continue;

                InsertIntoSqlite(entry);
                seeded++;
            }

            if (seeded > 0)
                _logger.LogInformation("从 .agentic-os/memory/ 明文文件 seed 了 {Count} 条记忆", seeded);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "从 .agentic-os/memory/ 文件 seed 记忆失败");
        }
    }

    private static MemoryEntry? FileToMemoryEntry(Dna.Knowledge.FileProtocol.Models.MemoryFile file)
    {
        if (string.IsNullOrWhiteSpace(file.Id))
            return null;

        var type = Enum.TryParse<MemoryType>(file.Type, ignoreCase: true, out var mt) ? mt : MemoryType.Semantic;
        var source = Enum.TryParse<MemorySource>(file.Source, ignoreCase: true, out var ms) ? ms : MemorySource.Human;

        return new MemoryEntry
        {
            Id = file.Id,
            Type = type,
            Source = source,
            Content = file.Body,
            Summary = null,
            NodeId = file.NodeId,
            Disciplines = file.Disciplines ?? [],
            Tags = file.Tags ?? [],
            Importance = file.Importance ?? 0.5,
            Stage = MemoryStage.LongTerm,
            CreatedAt = file.CreatedAt,
            LastVerifiedAt = file.LastVerifiedAt,
            SupersededBy = file.SupersededBy
        };
    }

    private static string? ResolveAgenticOsPathForMemory(string storePath)
    {
        if (string.IsNullOrWhiteSpace(storePath))
            return null;

        // storePath 可能是 .agentic-os/memory/ → 上一级
        var parent = Path.GetDirectoryName(storePath);
        if (parent != null)
        {
            var memoryDir = Path.Combine(parent, FileProtocolPaths.MemoryDir);
            if (Directory.Exists(memoryDir))
                return parent;
        }

        // storePath 本身是 .agentic-os/
        var directMemory = Path.Combine(storePath, FileProtocolPaths.MemoryDir);
        if (Directory.Exists(directMemory))
            return storePath;

        return null;
    }

    public void Dispose()
    {
        _db?.Dispose();
        _db = null;
        GC.SuppressFinalize(this);
    }
}
