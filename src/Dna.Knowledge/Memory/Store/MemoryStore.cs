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
    private string _entriesDir = string.Empty;

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
        _entriesDir = Path.Combine(_memoryDir, "entries");
        Directory.CreateDirectory(_entriesDir);

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
        EnsureGitIgnore();
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
                file_path       TEXT NOT NULL,
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
    }

    private void EnsureGitIgnore()
    {
        var gitignorePath = Path.Combine(_memoryDir, ".gitignore");
        if (!File.Exists(gitignorePath))
            File.WriteAllText(gitignorePath, "index.db\nindex.db-wal\nindex.db-shm\n");
    }

    /// <summary>如果 SQLite 为空但有 JSON 文件，从 JSON 重建索引</summary>
    private int RebuildFromJsonIfNeeded()
    {
        using var countCmd = _db!.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*) FROM memory_entries";
        var dbCount = Convert.ToInt32(countCmd.ExecuteScalar());

        if (dbCount > 0)
            return dbCount;

        var jsonFiles = Directory.Exists(_entriesDir)
            ? Directory.GetFiles(_entriesDir, "*.json", SearchOption.AllDirectories)
            : [];

        if (jsonFiles.Length == 0)
            return 0;

        _logger.LogInformation("SQLite 为空，从 {Count} 个 JSON 文件重建索引...", jsonFiles.Length);

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
                    InsertIntoSqlite(entry, file);
                    count++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "跳过损坏的 JSON: {File}", file);
            }
        }
        transaction.Commit();

        _logger.LogInformation("从 JSON 重建完成: {Count} 条记忆", count);
        return count;
    }

    // ═══════════════════════════════════════════
    //  索引重建（全量 / 增量）
    // ═══════════════════════════════════════════

    /// <summary>
    /// 全量重建：清空 SQLite 所有表，从 entries/*.json 重新导入。
    /// 适用于：git pull 后大量 JSON 变更、DB 疑似损坏、手动修改了 JSON 文件。
    /// rewriteJson=true 时会用当前序列化选项重写每个 JSON 文件（修复 Unicode 转义等格式问题）。
    /// </summary>
    public (int imported, int skipped) RebuildIndex(bool rewriteJson = false)
    {
        if (_db == null) throw new InvalidOperationException("MemoryStore not initialized");

        ClearAllSqliteTables();

        var jsonFiles = Directory.Exists(_entriesDir)
            ? Directory.GetFiles(_entriesDir, "*.json", SearchOption.AllDirectories)
            : [];

        if (jsonFiles.Length == 0)
            return (0, 0);

        _logger.LogInformation("全量重建索引: 扫描到 {Count} 个 JSON 文件, rewriteJson={Rewrite}", jsonFiles.Length, rewriteJson);

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
                    if (rewriteJson)
                        WriteJsonFile(entry);

                    InsertIntoSqlite(entry, file);
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

        _logger.LogInformation("全量重建完成: 导入 {Imported}, 跳过 {Skipped}", imported, skipped);
        return (imported, skipped);
    }

    /// <summary>
    /// 增量同步：只把 DB 中缺失的 JSON 文件补入索引，不清空已有数据。
    /// 适用于：git pull 后有少量新增 JSON、团队成员新写入了知识。
    /// 同时清理 DB 中指向已删除 JSON 的孤儿记录。
    /// </summary>
    public (int added, int removed, int skipped) SyncFromJson()
    {
        if (_db == null) throw new InvalidOperationException("MemoryStore not initialized");

        var jsonFiles = Directory.Exists(_entriesDir)
            ? Directory.GetFiles(_entriesDir, "*.json", SearchOption.AllDirectories)
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

        _logger.LogInformation("增量同步: 新增 {Add}, 移除孤儿 {Remove}, 跳过 {Skip}",
            toAdd.Count, toRemove.Count, skipped);

        using var transaction = _db.BeginTransaction();

        foreach (var id in toAdd)
        {
            try
            {
                var file = jsonIdToFile[id];
                var json = File.ReadAllText(file);
                var entry = JsonSerializer.Deserialize<MemoryEntry>(json, JsonOpts);
                if (entry != null)
                    InsertIntoSqlite(entry, file);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "增量同步跳过: {Id}", id);
                skipped++;
            }
        }

        foreach (var id in toRemove)
            DeleteFromSqlite(id);

        transaction.Commit();

        _logger.LogInformation("增量同步完成: 新增 {Added}, 移除 {Removed}", toAdd.Count, toRemove.Count);
        return (toAdd.Count, toRemove.Count, skipped);
    }

    /// <summary>
    /// 从 SQLite 反写 JSON：将 DB 中的全部记忆导出为 entries/*.json 文件。
    /// 适用于：JSON 文件损坏/丢失后从索引恢复、修复 Unicode 转义为明文中文。
    /// 已有的 JSON 文件会被覆盖。
    /// </summary>
    public (int exported, int skipped) ExportToJson()
    {
        if (_db == null) throw new InvalidOperationException("MemoryStore not initialized");

        using var cmd = _db.CreateCommand();
        cmd.CommandText = @"
            SELECT e.id, e.type, e.layer, e.source, e.summary, e.importance, e.freshness,
                   e.created_at, e.last_verified_at, e.stale_after, e.superseded_by,
                   e.parent_id, e.version, e.file_path, e.ext_source_url, e.ext_source_id,
                   f.content, e.node_id
            FROM memory_entries e
            LEFT JOIN memory_fts f ON e.id = f.id";

        var rows = new List<(string id, string type, string layer, string source,
            string? summary, double importance, string freshness,
            string createdAt, string? lastVerifiedAt, string? staleAfter, string? supersededBy,
            string? parentId, int version, string filePath, string? extSourceUrl, string? extSourceId,
            string content, string? nodeId)>();

        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                rows.Add((
                    reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.GetDouble(5), reader.GetString(6), reader.GetString(7),
                    reader.IsDBNull(8) ? null : reader.GetString(8),
                    reader.IsDBNull(9) ? null : reader.GetString(9),
                    reader.IsDBNull(10) ? null : reader.GetString(10),
                    reader.IsDBNull(11) ? null : reader.GetString(11),
                    reader.GetInt32(12), reader.GetString(13),
                    reader.IsDBNull(14) ? null : reader.GetString(14),
                    reader.IsDBNull(15) ? null : reader.GetString(15),
                    reader.IsDBNull(16) ? "" : reader.GetString(16),
                    reader.IsDBNull(17) ? null : reader.GetString(17)
                ));
            }
        }

        if (rows.Count == 0)
        {
            _logger.LogInformation("ExportToJson: DB 为空，无记忆可导出");
            return (0, 0);
        }

        _logger.LogInformation("ExportToJson: 从 DB 导出 {Count} 条记忆到 JSON", rows.Count);

        var exported = 0;
        var skipped = 0;
        foreach (var r in rows)
        {
            try
            {
                if (!Enum.TryParse<MemoryType>(r.type, true, out var memType)) { skipped++; continue; }
                if (!Enum.TryParse<KnowledgeLayer>(r.layer, true, out var kLayer)) { skipped++; continue; }
                if (!Enum.TryParse<MemorySource>(r.source, true, out var memSource)) { skipped++; continue; }
                if (!Enum.TryParse<FreshnessStatus>(r.freshness, true, out var fresh)) fresh = FreshnessStatus.Fresh;

                var entry = new MemoryEntry
                {
                    Id = r.id,
                    Type = memType,
                    Layer = kLayer,
                    Source = memSource,
                    Summary = r.summary,
                    Content = r.content,
                    Importance = r.importance,
                    Freshness = fresh,
                    CreatedAt = DateTime.TryParse(r.createdAt, out var ca) ? ca : DateTime.UtcNow,
                    LastVerifiedAt = r.lastVerifiedAt != null && DateTime.TryParse(r.lastVerifiedAt, out var lv) ? lv : null,
                    StaleAfter = r.staleAfter != null && DateTime.TryParse(r.staleAfter, out var sa) ? sa : null,
                    SupersededBy = r.supersededBy,
                    ParentId = r.parentId,
                    Version = r.version,
                    ExternalSourceUrl = r.extSourceUrl,
                    ExternalSourceId = r.extSourceId,
                    Disciplines = QueryMultiValues("memory_disciplines", "discipline", r.id),
                    Features = QueryMultiValues("memory_features", "feature", r.id),
                    NodeId = r.nodeId,
                    Tags = QueryMultiValues("memory_tags", "tag", r.id),
                    PathPatterns = QueryMultiValues("memory_paths", "path_pattern", r.id),
                    RelatedIds = QueryMultiValues("memory_relations_export", "to_id", r.id),
                };

                WriteJsonFile(entry);
                exported++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ExportToJson 跳过: {Id}", r.id);
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

    /// <summary>写入一条记忆（同时写 JSON 文件 + SQLite 索引 + FTS）</summary>
    public MemoryEntry Insert(MemoryEntry entry)
    {
        var filePath = WriteJsonFile(entry);
        InsertIntoSqlite(entry, filePath);
        return entry;
    }

    /// <summary>更新一条记忆</summary>
    public MemoryEntry Update(MemoryEntry entry)
    {
        var filePath = WriteJsonFile(entry);
        DeleteFromSqlite(entry.Id);
        InsertIntoSqlite(entry, filePath);
        return entry;
    }

    /// <summary>删除一条记忆</summary>
    public bool Delete(string id)
    {
        var entry = GetById(id);
        if (entry == null) return false;

        DeleteJsonFile(id);
        DeleteFromSqlite(id);
        return true;
    }

    /// <summary>批量写入（事务）</summary>
    public List<MemoryEntry> InsertBatch(List<MemoryEntry> entries)
    {
        using var transaction = _db!.BeginTransaction();
        foreach (var entry in entries)
        {
            var filePath = WriteJsonFile(entry);
            InsertIntoSqlite(entry, filePath);
        }
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

        UpdateJsonFieldIfExists(id, e => e.Freshness = freshness);
    }

    public void UpdateTags(string id, List<string> tags)
    {
        using var cmd = _db!.CreateCommand();
        cmd.CommandText = "UPDATE memory_entries SET tags = @tags WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@tags", string.Join(",", tags));
        cmd.ExecuteNonQuery();

        UpdateJsonFieldIfExists(id, e =>
        {
            e.Tags.Clear();
            e.Tags.AddRange(tags);
        });
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
                UpdateJsonFieldIfExists(id, e => e.Freshness = FreshnessStatus.Aging);
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

        UpdateJsonFieldIfExists(id, e =>
        {
            e.Freshness = FreshnessStatus.Fresh;
            e.LastVerifiedAt = now;
        });
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

    /// <summary>按 ID 获取完整记忆（从 JSON 读取）</summary>
    public MemoryEntry? GetById(string id)
    {
        using var cmd = _db!.CreateCommand();
        cmd.CommandText = "SELECT file_path FROM memory_entries WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);

        var filePath = cmd.ExecuteScalar() as string;
        if (filePath == null || !File.Exists(filePath))
            return null;

        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<MemoryEntry>(json, JsonOpts);
    }

    /// <summary>结构化查询</summary>
    public List<MemoryEntry> Query(MemoryFilter filter)
    {
        var (whereClause, parameters) = BuildWhereClause(filter);
        var sql = $"""
            SELECT DISTINCT e.file_path FROM memory_entries e
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
    //  内部：JSON 文件操作
    // ═══════════════════════════════════════════

    private string WriteJsonFile(MemoryEntry entry)
    {
        var yearMonth = entry.CreatedAt.ToString("yyyy-MM");
        var dir = Path.Combine(_entriesDir, yearMonth);
        Directory.CreateDirectory(dir);

        var filePath = Path.Combine(dir, $"{entry.Id}.json");
        var json = JsonSerializer.Serialize(entry, JsonOpts);
        File.WriteAllText(filePath, json);
        return filePath;
    }

    private void DeleteJsonFile(string id)
    {
        using var cmd = _db!.CreateCommand();
        cmd.CommandText = "SELECT file_path FROM memory_entries WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);

        var filePath = cmd.ExecuteScalar() as string;
        if (filePath != null && File.Exists(filePath))
            File.Delete(filePath);
    }

    private void UpdateJsonFieldIfExists(string id, Action<MemoryEntry> mutate)
    {
        var entry = GetById(id);
        if (entry == null) return;
        mutate(entry);
        WriteJsonFile(entry);
    }

    // ═══════════════════════════════════════════
    //  内部：SQLite 操作
    // ═══════════════════════════════════════════

    private void InsertIntoSqlite(MemoryEntry entry, string filePath)
    {
        using var cmd = _db!.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO memory_entries
                (id, type, layer, source, summary, importance, freshness,
                 created_at, last_verified_at, stale_after, superseded_by,
                 parent_id, node_id, version, file_path, embedding, ext_source_url, ext_source_id)
            VALUES
                (@id, @type, @layer, @source, @summary, @importance, @freshness,
                 @created_at, @last_verified_at, @stale_after, @superseded_by,
                 @parent_id, @node_id, @version, @file_path, @embedding, @ext_source_url, @ext_source_id)
            """;

        cmd.Parameters.AddWithValue("@id", entry.Id);
        cmd.Parameters.AddWithValue("@type", entry.Type.ToString());
        cmd.Parameters.AddWithValue("@layer", entry.Layer.ToString());
        cmd.Parameters.AddWithValue("@source", entry.Source.ToString());
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
        cmd.Parameters.AddWithValue("@file_path", filePath);
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

    private List<MemoryEntry> ReadEntriesFromCommand(SqliteCommand cmd)
    {
        var entries = new List<MemoryEntry>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var filePath = reader.GetString(0);
            if (!File.Exists(filePath)) continue;

            try
            {
                var json = File.ReadAllText(filePath);
                var entry = JsonSerializer.Deserialize<MemoryEntry>(json, JsonOpts);
                if (entry != null) entries.Add(entry);
            }
            catch { /* skip corrupted */ }
        }
        return entries;
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
