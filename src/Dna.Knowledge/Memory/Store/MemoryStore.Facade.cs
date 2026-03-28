using Dna.Core.Config;
using Dna.Knowledge;
using Dna.Knowledge.Models;
using Dna.Knowledge.Project.Models;
using Dna.Memory.Models;
using Dna.Memory.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dna.Memory.Store;

/// <summary>
/// MemoryStore 对外 API — 记忆系统的唯一公开接口。
/// 内部组件（Writer/Reader/RecallEngine/VectorIndex/EmbeddingService）
/// 在 BuildInternals 时一次性创建，不暴露到外部。
/// </summary>
internal partial class MemoryStore
{
    private MemoryWriter? _writer;
    private MemoryReader? _reader;
    private MemoryRecallEngine? _recallEngine;
    private readonly object _manifestLock = new();
    private ArchitectureManifest _architecture = new();
    private ModulesManifest _manifest = new();
    private ComputedManifest _computed = new();
    private string _graphDbPath = string.Empty;
    private static readonly JsonSerializerOptions ModuleManifestJsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    /// <summary>
    /// 构建所有内部组件。在 Initialize 之后、首次使用之前调用一次。
    /// 所有内部对象在此创建，不经过外部 DI 容器。
    /// </summary>
    internal void BuildInternals(
        IHttpClientFactory httpClientFactory,
        ProjectConfig config,
        ILoggerFactory loggerFactory)
    {
        var vectorIndex = new VectorIndex();
        var embeddingService = new EmbeddingService(httpClientFactory, config,
            loggerFactory.CreateLogger<EmbeddingService>());

        _writer = new MemoryWriter(this, embeddingService, vectorIndex,
            loggerFactory.CreateLogger<MemoryWriter>());
        _reader = new MemoryReader(this, loggerFactory.CreateLogger<MemoryReader>());
        _recallEngine = new MemoryRecallEngine(this, vectorIndex, embeddingService,
            loggerFactory.CreateLogger<MemoryRecallEngine>());
    }

    // ═══════════════════════════════════════════
    //  写入 API
    // ═══════════════════════════════════════════

    /// <summary>写入一条记忆（自动生成 embedding + 索引同步）</summary>
    public Task<MemoryEntry> RememberAsync(RememberRequest request)
        => Writer.RememberAsync(request);

    /// <summary>更新一条记忆</summary>
    public Task<MemoryEntry> UpdateMemoryAsync(string memoryId, RememberRequest request)
        => Writer.UpdateAsync(memoryId, request);

    /// <summary>标记一条记忆被新知识取代</summary>
    public void Supersede(string oldMemoryId, string newMemoryId)
        => Writer.Supersede(oldMemoryId, newMemoryId);

    /// <summary>批量写入</summary>
    public Task<List<MemoryEntry>> RememberBatchAsync(List<RememberRequest> requests)
        => Writer.RememberBatchAsync(requests);

    /// <summary>验证一条记忆仍有效，重置鲜活度为 Fresh</summary>
    public void VerifyMemory(string memoryId)
        => Writer.Verify(memoryId);

    // ═══════════════════════════════════════════
    //  查询 API
    // ═══════════════════════════════════════════

    /// <summary>获取业务系统的全职能知识汇总</summary>
    public FeatureKnowledgeSummary GetFeatureSummary(string featureId)
        => Reader.GetFeatureSummary(featureId);

    /// <summary>获取职能知识汇总（按层级分组）</summary>
    public DisciplineKnowledgeSummary GetDisciplineSummary(string disciplineId)
        => Reader.GetDisciplineSummary(disciplineId);

    // ═══════════════════════════════════════════
    //  语义检索 API
    // ═══════════════════════════════════════════

    /// <summary>语义召回 — 四通道检索 + 融合排序 + 约束链展开</summary>
    public Task<RecallResult> RecallAsync(RecallQuery query)
        => RecallEngine.RecallAsync(query);

    // ═══════════════════════════════════════════
    //  模块注册 API
    // ═══════════════════════════════════════════

    // ═══════════════════════════════════════════
    //  架构定义 API（architecture.json）
    // ═══════════════════════════════════════════

    public ArchitectureManifest GetArchitecture()
    {
        lock (_manifestLock) return _architecture;
    }

    public void UpsertDiscipline(string disciplineId, string? displayName, string roleId, List<LayerDefinition> layers)
    {
        if (string.IsNullOrWhiteSpace(disciplineId))
            throw new ArgumentException("disciplineId 不能为空");

        lock (_manifestLock)
        {
            if (!_architecture.Disciplines.TryGetValue(disciplineId, out var def))
            {
                def = new DisciplineDefinition();
                _architecture.Disciplines[disciplineId] = def;
            }

            def.DisplayName = displayName?.Trim();
            def.RoleId = string.IsNullOrWhiteSpace(roleId) ? "coder" : roleId.Trim();
            def.Layers = layers
                .Where(l => !string.IsNullOrWhiteSpace(l.Name))
                .OrderBy(l => l.Level)
                .ToList();

            SaveArchitectureLocked();
            _logger.LogInformation("部门已更新: {Discipline}, RoleId={Role}, Layers={Count}",
                disciplineId, def.RoleId, def.Layers.Count);
        }
    }

    public bool RemoveDiscipline(string disciplineId)
    {
        lock (_manifestLock)
        {
            if (!_architecture.Disciplines.Remove(disciplineId))
                return false;

            _manifest.Disciplines.Remove(disciplineId);
            SaveArchitectureLocked();
            SaveModulesManifestLocked();
            _logger.LogInformation("部门已删除: {Discipline}", disciplineId);
            return true;
        }
    }

    // ═══════════════════════════════════════════
    //  模块注册 API（modules.json）
    // ═══════════════════════════════════════════

    public ModulesManifest GetModulesManifest()
    {
        lock (_manifestLock) return _manifest;
    }

    public void ReplaceModulesManifest(ModulesManifest manifest)
    {
        lock (_manifestLock)
        {
            _manifest = manifest;
            SaveModulesManifestLocked();
            _logger.LogInformation("modules.json 已整体替换，模块数: {Count}",
                manifest.Disciplines.Values.Sum(d => d.Count));
        }
    }

    public ComputedManifest GetComputedManifest()
    {
        lock (_manifestLock) return _computed;
    }

    public Dictionary<string, NodeKnowledge> LoadNodeKnowledgeMap()
    {
        lock (_manifestLock)
        {
            EnsureGraphSchemaLocked();
            var map = new Dictionary<string, NodeKnowledge>(StringComparer.OrdinalIgnoreCase);

            using var conn = CreateGraphConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT node_id, identity, lessons_json, active_tasks_json, facts_json, total_memory_count, memory_ids_json
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
                    MemoryIds = DeserializeOrDefault(reader.IsDBNull(6) ? "[]" : reader.GetString(6), new List<string>())
                };
            }

            return map;
        }
    }

    public void UpsertNodeKnowledge(string nodeId, NodeKnowledge knowledge)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
            throw new ArgumentException("nodeId 不能为空", nameof(nodeId));

        lock (_manifestLock)
        {
            EnsureGraphSchemaLocked();
            using var conn = CreateGraphConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO graph_node_knowledge
                    (node_id, identity, lessons_json, active_tasks_json, facts_json, total_memory_count, memory_ids_json, updated_at)
                VALUES
                    (@node, @identity, @lessons, @tasks, @facts, @total, @memoryIds, @updatedAt)
                ON CONFLICT(node_id) DO UPDATE SET
                    identity = excluded.identity,
                    lessons_json = excluded.lessons_json,
                    active_tasks_json = excluded.active_tasks_json,
                    facts_json = excluded.facts_json,
                    total_memory_count = excluded.total_memory_count,
                    memory_ids_json = excluded.memory_ids_json,
                    updated_at = excluded.updated_at
                """;
            cmd.Parameters.AddWithValue("@node", nodeId);
            cmd.Parameters.AddWithValue("@identity", (object?)knowledge.Identity ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@lessons", JsonSerializer.Serialize(knowledge.Lessons ?? [], ModuleManifestJsonOpts));
            cmd.Parameters.AddWithValue("@tasks", JsonSerializer.Serialize(knowledge.ActiveTasks ?? [], ModuleManifestJsonOpts));
            cmd.Parameters.AddWithValue("@facts", JsonSerializer.Serialize(knowledge.Facts ?? [], ModuleManifestJsonOpts));
            cmd.Parameters.AddWithValue("@total", Math.Max(knowledge.TotalMemoryCount, 0));
            cmd.Parameters.AddWithValue("@memoryIds", JsonSerializer.Serialize(knowledge.MemoryIds ?? [], ModuleManifestJsonOpts));
            cmd.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();
        }
    }

    public void RegisterModule(string discipline, ModuleRegistration module)
    {
        if (string.IsNullOrWhiteSpace(module.Id))
            module.Id = UlidGenerator.New();

        lock (_manifestLock)
        {
            if (!_architecture.Disciplines.ContainsKey(discipline))
            {
                _architecture.Disciplines[discipline] = new DisciplineDefinition
                {
                    DisplayName = discipline,
                    RoleId = "coder",
                    Layers = []
                };
            }

            if (!_manifest.Disciplines.TryGetValue(discipline, out var modules))
            {
                modules = [];
                _manifest.Disciplines[discipline] = modules;
            }

            var existing = modules.FindIndex(m =>
                string.Equals(m.Name, module.Name, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0)
                modules[existing] = module;
            else
                modules.Add(module);

            SaveModulesManifestLocked();
            _logger.LogInformation("模块已注册: {Module} → {Discipline}", module.Name, discipline);
        }
    }

    public bool UnregisterModule(string name)
    {
        lock (_manifestLock)
        {
            foreach (var (discipline, modules) in _manifest.Disciplines)
            {
                var removed = modules.RemoveAll(m =>
                    string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
                if (removed > 0)
                {
                    SaveModulesManifestLocked();
                    _logger.LogInformation("模块已注销: {Module} (from {Discipline})", name, discipline);
                    return true;
                }
            }
            return false;
        }
    }

    public void RegisterFeature(string featureId, FeatureDefinition feature)
    {
        lock (_manifestLock)
        {
            _manifest.Features[featureId] = feature;
            SaveModulesManifestLocked();
        }
    }

    /// <summary>更新模块的计算依赖（事实依赖）</summary>
    public void UpdateComputedDependencies(string moduleName, List<string> computedDependencies)
    {
        lock (_manifestLock)
        {
            _computed.ModuleDependencies[moduleName] = computedDependencies
                .Where(d => !string.IsNullOrWhiteSpace(d))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            SaveComputedManifestLocked();
            _logger.LogInformation("模块计算依赖已更新: {Module} => [{Deps}]",
                moduleName, string.Join(", ", _computed.ModuleDependencies[moduleName]));
        }
    }

    /// <summary>保存或更新一条 CrossWork 声明</summary>
    public void SaveCrossWork(CrossWorkRegistration crossWork)
    {
        lock (_manifestLock)
        {
            if (string.IsNullOrWhiteSpace(crossWork.Id))
                crossWork.Id = NewId();

            var index = _manifest.CrossWorks.FindIndex(cw =>
                string.Equals(cw.Id, crossWork.Id, StringComparison.OrdinalIgnoreCase));

            if (index >= 0)
                _manifest.CrossWorks[index] = crossWork;
            else
                _manifest.CrossWorks.Add(crossWork);

            SaveModulesManifestLocked();
        }
    }

    /// <summary>删除一条 CrossWork 声明</summary>
    public bool RemoveCrossWork(string crossWorkId)
    {
        lock (_manifestLock)
        {
            var removed = _manifest.CrossWorks.RemoveAll(cw =>
                string.Equals(cw.Id, crossWorkId, StringComparison.OrdinalIgnoreCase));
            if (removed > 0)
                SaveModulesManifestLocked();
            return removed > 0;
        }
    }

    /// <summary>
    /// 将 nodeId 解析为“模块 ID 优先”的候选集合。
    /// - 命中模块 ID: 返回 [id, name]（兼容历史上按 name 存储的记录）
    /// - 命中模块 Name: 返回 [id, name]
    /// - 未命中:
    ///   - strict=true: 抛错
    ///   - strict=false: 返回原值（兼容非模块节点或历史脏数据查询）
    /// </summary>
    internal List<string> ResolveNodeIdCandidates(string? nodeId, bool strict = false)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
            return [];

        var input = nodeId.Trim();

        lock (_manifestLock)
        {
            foreach (var modules in _manifest.Disciplines.Values)
            {
                var byId = modules.FirstOrDefault(m =>
                    string.Equals(m.Id, input, StringComparison.OrdinalIgnoreCase));
                if (byId != null)
                {
                    return [byId.Id, byId.Name];
                }
            }

            foreach (var modules in _manifest.Disciplines.Values)
            {
                var byName = modules.FirstOrDefault(m =>
                    string.Equals(m.Name, input, StringComparison.OrdinalIgnoreCase));
                if (byName != null)
                {
                    return [byName.Id, byName.Name];
                }
            }
        }

        if (strict)
            throw new InvalidOperationException($"nodeId '{input}' 不存在于已注册模块中。请传模块 Id 或 Name。");

        return [input];
    }

    // ═══════════════════════════════════════════

    /// <summary>生成一个新的 ULID（时间有序全局唯一 ID）</summary>
    public static string NewId() => UlidGenerator.New();

    private MemoryWriter Writer => _writer ?? throw new InvalidOperationException("MemoryStore not initialized. Call BuildInternals first.");
    private MemoryReader Reader => _reader ?? throw new InvalidOperationException("MemoryStore not initialized. Call BuildInternals first.");
    private MemoryRecallEngine RecallEngine => _recallEngine ?? throw new InvalidOperationException("MemoryStore not initialized. Call BuildInternals first.");

    internal void LoadAllManifests()
    {
        lock (_manifestLock)
        {
            _graphDbPath = Path.Combine(_storePath, "graph.db");
            EnsureGraphSchemaLocked();
            LoadGraphSnapshotLocked();

            if (IsGraphSnapshotEmpty() && TryLoadLegacyJsonLocked())
            {
                SaveGraphSnapshotLocked();
                CleanupLegacyJsonFilesLocked();
            }

            EnsureDerivedDisciplinesLocked();

            _logger.LogInformation(
                "图谱加载完成: graphDb={GraphDb}, 部门={Depts}, 模块={Mods}, 计算依赖={Facts}",
                _graphDbPath,
                _architecture.Disciplines.Count,
                _manifest.Disciplines.Values.Sum(d => d.Count),
                _computed.ModuleDependencies.Count);
        }
    }

    private T? LoadJsonFile<T>(string path) where T : class
    {
        if (!File.Exists(path)) return null;
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(json, ModuleManifestJsonOpts);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{File} 解析失败", System.IO.Path.GetFileName(path));
            return null;
        }
    }

    private void MigrateLegacy(string legacyPath)
    {
        try
        {
            var json = File.ReadAllText(legacyPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("disciplines", out var discs))
            {
                foreach (var prop in discs.EnumerateObject())
                {
                    var id = prop.Name;
                    var def = new DisciplineDefinition();
                    var modules = new List<ModuleRegistration>();

                    if (prop.Value.TryGetProperty("displayName", out var dn))
                        def.DisplayName = dn.GetString();

                    if (prop.Value.TryGetProperty("layers", out var layers))
                        def.Layers = JsonSerializer.Deserialize<List<LayerDefinition>>(layers.GetRawText(), ModuleManifestJsonOpts) ?? [];

                    if (prop.Value.TryGetProperty("modules", out var mods))
                        modules = JsonSerializer.Deserialize<List<ModuleRegistration>>(mods.GetRawText(), ModuleManifestJsonOpts) ?? [];

                    _architecture.Disciplines[id] = def;
                    _manifest.Disciplines[id] = modules;
                }
            }

            if (root.TryGetProperty("crossWorks", out var cw))
                _manifest.CrossWorks = JsonSerializer.Deserialize<List<CrossWorkRegistration>>(cw.GetRawText(), ModuleManifestJsonOpts) ?? [];

            if (root.TryGetProperty("features", out var ft))
                _manifest.Features = JsonSerializer.Deserialize<Dictionary<string, FeatureDefinition>>(ft.GetRawText(), ModuleManifestJsonOpts) ?? new();

            _logger.LogInformation("已解析旧格式清单: {Path}", legacyPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "旧格式迁移失败");
        }
    }

    private void SaveArchitectureLocked()
    {
        SaveGraphSnapshotLocked();
    }

    private void SaveModulesManifestLocked()
    {
        SaveGraphSnapshotLocked();
    }

    private void SaveComputedManifestLocked()
    {
        SaveGraphSnapshotLocked();
    }

    private SqliteConnection CreateGraphConnection()
    {
        var conn = new SqliteConnection($"Data Source={_graphDbPath}");
        conn.Open();
        return conn;
    }

    private void EnsureGraphSchemaLocked()
    {
        using var conn = CreateGraphConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS graph_disciplines (
                id TEXT PRIMARY KEY,
                display_name TEXT,
                role_id TEXT NOT NULL,
                layers_json TEXT NOT NULL DEFAULT '[]'
            );

            CREATE TABLE IF NOT EXISTS graph_modules (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL UNIQUE,
                discipline TEXT NOT NULL,
                path TEXT NOT NULL,
                layer INTEGER NOT NULL DEFAULT 0,
                is_crosswork_module INTEGER NOT NULL DEFAULT 0,
                participants_json TEXT NOT NULL DEFAULT '[]',
                dependencies_json TEXT NOT NULL DEFAULT '[]',
                maintainer TEXT,
                summary TEXT,
                boundary TEXT,
                public_api_json TEXT,
                constraints_json TEXT,
                metadata_json TEXT
            );

            CREATE TABLE IF NOT EXISTS graph_crossworks (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                description TEXT,
                feature TEXT,
                participants_json TEXT NOT NULL DEFAULT '[]'
            );

            CREATE TABLE IF NOT EXISTS graph_features (
                id TEXT PRIMARY KEY,
                payload_json TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS graph_computed_dependencies (
                module_name TEXT PRIMARY KEY,
                dependencies_json TEXT NOT NULL DEFAULT '[]'
            );

            CREATE TABLE IF NOT EXISTS graph_node_knowledge (
                node_id TEXT PRIMARY KEY,
                identity TEXT,
                lessons_json TEXT NOT NULL DEFAULT '[]',
                active_tasks_json TEXT NOT NULL DEFAULT '[]',
                facts_json TEXT NOT NULL DEFAULT '[]',
                total_memory_count INTEGER NOT NULL DEFAULT 0,
                memory_ids_json TEXT NOT NULL DEFAULT '[]',
                updated_at TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }

    private void LoadGraphSnapshotLocked()
    {
        _architecture = new ArchitectureManifest();
        _manifest = new ModulesManifest();
        _computed = new ComputedManifest();

        using var conn = CreateGraphConnection();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT id, display_name, role_id, layers_json FROM graph_disciplines";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var id = reader.GetString(0);
                var def = new DisciplineDefinition
                {
                    DisplayName = reader.IsDBNull(1) ? null : reader.GetString(1),
                    RoleId = reader.IsDBNull(2) ? "coder" : reader.GetString(2),
                    Layers = DeserializeOrDefault(reader.IsDBNull(3) ? "[]" : reader.GetString(3), new List<LayerDefinition>())
                };
                _architecture.Disciplines[id] = def;
            }
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT id, name, discipline, path, layer, is_crosswork_module,
                       participants_json, dependencies_json, maintainer, summary,
                       boundary, public_api_json, constraints_json, metadata_json
                FROM graph_modules
                """;
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var discipline = reader.GetString(2);
                if (!_manifest.Disciplines.TryGetValue(discipline, out var modules))
                {
                    modules = [];
                    _manifest.Disciplines[discipline] = modules;
                }

                var module = new ModuleRegistration
                {
                    Id = reader.GetString(0),
                    Name = reader.GetString(1),
                    Path = reader.GetString(3),
                    Layer = reader.GetInt32(4),
                    IsCrossWorkModule = reader.GetInt64(5) == 1,
                    Participants = DeserializeOrDefault(reader.IsDBNull(6) ? "[]" : reader.GetString(6), new List<CrossWorkParticipantRegistration>()),
                    Dependencies = DeserializeOrDefault(reader.IsDBNull(7) ? "[]" : reader.GetString(7), new List<string>()),
                    Maintainer = reader.IsDBNull(8) ? null : reader.GetString(8),
                    Summary = reader.IsDBNull(9) ? null : reader.GetString(9),
                    Boundary = reader.IsDBNull(10) ? null : reader.GetString(10),
                    PublicApi = reader.IsDBNull(11) ? null : DeserializeOrDefault<List<string>>(reader.GetString(11), []),
                    Constraints = reader.IsDBNull(12) ? null : DeserializeOrDefault<List<string>>(reader.GetString(12), []),
                    Metadata = reader.IsDBNull(13) ? null : DeserializeOrDefault<Dictionary<string, string>>(reader.GetString(13), new Dictionary<string, string>())
                };
                modules.Add(module);
            }
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT id, name, description, feature, participants_json FROM graph_crossworks";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                _manifest.CrossWorks.Add(new CrossWorkRegistration
                {
                    Id = reader.GetString(0),
                    Name = reader.GetString(1),
                    Description = reader.IsDBNull(2) ? null : reader.GetString(2),
                    Feature = reader.IsDBNull(3) ? null : reader.GetString(3),
                    Participants = DeserializeOrDefault(reader.IsDBNull(4) ? "[]" : reader.GetString(4), new List<CrossWorkParticipantRegistration>())
                });
            }
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT id, payload_json FROM graph_features";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var id = reader.GetString(0);
                var feature = DeserializeOrDefault(reader.GetString(1), new FeatureDefinition());
                _manifest.Features[id] = feature;
            }
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT module_name, dependencies_json FROM graph_computed_dependencies";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                _computed.ModuleDependencies[reader.GetString(0)] =
                    DeserializeOrDefault(reader.GetString(1), new List<string>());
            }
        }
    }

    private void SaveGraphSnapshotLocked()
    {
        EnsureGraphSchemaLocked();

        using var conn = CreateGraphConnection();
        using var tx = conn.BeginTransaction();

        using (var clear = conn.CreateCommand())
        {
            clear.Transaction = tx;
            clear.CommandText = """
                DELETE FROM graph_disciplines;
                DELETE FROM graph_modules;
                DELETE FROM graph_crossworks;
                DELETE FROM graph_features;
                DELETE FROM graph_computed_dependencies;
                """;
            clear.ExecuteNonQuery();
        }

        foreach (var (id, def) in _architecture.Disciplines)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO graph_disciplines (id, display_name, role_id, layers_json)
                VALUES (@id, @display, @role, @layers)
                """;
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@display", (object?)def.DisplayName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@role", string.IsNullOrWhiteSpace(def.RoleId) ? "coder" : def.RoleId);
            cmd.Parameters.AddWithValue("@layers", JsonSerializer.Serialize(def.Layers ?? [], ModuleManifestJsonOpts));
            cmd.ExecuteNonQuery();
        }

        foreach (var (discipline, modules) in _manifest.Disciplines)
        {
            foreach (var module in modules)
            {
                if (string.IsNullOrWhiteSpace(module.Id))
                    module.Id = NewId();

                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO graph_modules
                        (id, name, discipline, path, layer, is_crosswork_module,
                         participants_json, dependencies_json, maintainer, summary,
                         boundary, public_api_json, constraints_json, metadata_json)
                    VALUES
                        (@id, @name, @discipline, @path, @layer, @isCw,
                         @participants, @dependencies, @maintainer, @summary,
                         @boundary, @publicApi, @constraints, @metadata)
                    """;
                cmd.Parameters.AddWithValue("@id", module.Id);
                cmd.Parameters.AddWithValue("@name", module.Name);
                cmd.Parameters.AddWithValue("@discipline", discipline);
                cmd.Parameters.AddWithValue("@path", module.Path);
                cmd.Parameters.AddWithValue("@layer", module.Layer);
                cmd.Parameters.AddWithValue("@isCw", module.IsCrossWorkModule ? 1 : 0);
                cmd.Parameters.AddWithValue("@participants", JsonSerializer.Serialize(module.Participants ?? [], ModuleManifestJsonOpts));
                cmd.Parameters.AddWithValue("@dependencies", JsonSerializer.Serialize(module.Dependencies ?? [], ModuleManifestJsonOpts));
                cmd.Parameters.AddWithValue("@maintainer", (object?)module.Maintainer ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@summary", (object?)module.Summary ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@boundary", (object?)module.Boundary ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@publicApi", module.PublicApi == null ? DBNull.Value : JsonSerializer.Serialize(module.PublicApi, ModuleManifestJsonOpts));
                cmd.Parameters.AddWithValue("@constraints", module.Constraints == null ? DBNull.Value : JsonSerializer.Serialize(module.Constraints, ModuleManifestJsonOpts));
                cmd.Parameters.AddWithValue("@metadata", module.Metadata == null ? DBNull.Value : JsonSerializer.Serialize(module.Metadata, ModuleManifestJsonOpts));
                cmd.ExecuteNonQuery();
            }
        }

        foreach (var cw in _manifest.CrossWorks)
        {
            if (string.IsNullOrWhiteSpace(cw.Id))
                cw.Id = NewId();

            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO graph_crossworks (id, name, description, feature, participants_json)
                VALUES (@id, @name, @description, @feature, @participants)
                """;
            cmd.Parameters.AddWithValue("@id", cw.Id);
            cmd.Parameters.AddWithValue("@name", cw.Name);
            cmd.Parameters.AddWithValue("@description", (object?)cw.Description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@feature", (object?)cw.Feature ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@participants", JsonSerializer.Serialize(cw.Participants ?? [], ModuleManifestJsonOpts));
            cmd.ExecuteNonQuery();
        }

        foreach (var (id, feature) in _manifest.Features)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO graph_features (id, payload_json) VALUES (@id, @payload)";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@payload", JsonSerializer.Serialize(feature, ModuleManifestJsonOpts));
            cmd.ExecuteNonQuery();
        }

        foreach (var (moduleName, deps) in _computed.ModuleDependencies)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO graph_computed_dependencies (module_name, dependencies_json)
                VALUES (@module, @deps)
                """;
            cmd.Parameters.AddWithValue("@module", moduleName);
            cmd.Parameters.AddWithValue("@deps", JsonSerializer.Serialize(deps ?? [], ModuleManifestJsonOpts));
            cmd.ExecuteNonQuery();
        }

        // 清理已删除模块的知识物化数据，避免长期累积孤儿记录。
        using (var cleanup = conn.CreateCommand())
        {
            cleanup.Transaction = tx;
            cleanup.CommandText = """
                DELETE FROM graph_node_knowledge
                WHERE node_id NOT IN (SELECT id FROM graph_modules)
                """;
            cleanup.ExecuteNonQuery();
        }

        tx.Commit();
    }

    private bool IsGraphSnapshotEmpty()
    {
        return _architecture.Disciplines.Count == 0
            && _manifest.Disciplines.Count == 0
            && _manifest.CrossWorks.Count == 0
            && _manifest.Features.Count == 0
            && _computed.ModuleDependencies.Count == 0;
    }

    private bool TryLoadLegacyJsonLocked()
    {
        var baseDir = _storePath;
        var archPath = Path.Combine(baseDir, "architecture.json");
        var modulesPath = Path.Combine(baseDir, "modules.json");
        var computedPath = Path.Combine(baseDir, "modules.computed.json");
        var legacyManifestPath = Path.Combine(baseDir, "modules.manifest.json");

        var loaded = false;

        var arch = LoadJsonFile<ArchitectureManifest>(archPath);
        if (arch != null)
        {
            _architecture = arch;
            loaded = true;
        }

        var modules = LoadJsonFile<ModulesManifest>(modulesPath);
        if (modules != null)
        {
            _manifest = modules;
            loaded = true;
        }

        var computed = LoadJsonFile<ComputedManifest>(computedPath);
        if (computed != null)
        {
            _computed = computed;
            loaded = true;
        }

        if (!loaded && File.Exists(legacyManifestPath))
        {
            MigrateLegacy(legacyManifestPath);
            loaded = true;
        }

        return loaded;
    }

    private void CleanupLegacyJsonFilesLocked()
    {
        foreach (var path in new[]
        {
            Path.Combine(_storePath, "architecture.json"),
            Path.Combine(_storePath, "modules.json"),
            Path.Combine(_storePath, "modules.computed.json"),
            Path.Combine(_storePath, "modules.manifest.json")
        })
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "删除旧清单文件失败: {File}", path);
            }
        }
    }

    private void EnsureDerivedDisciplinesLocked()
    {
        foreach (var (discipline, modules) in _manifest.Disciplines)
        {
            if (!_architecture.Disciplines.TryGetValue(discipline, out var def))
            {
                def = new DisciplineDefinition
                {
                    DisplayName = discipline,
                    RoleId = "coder",
                    Layers = []
                };
                _architecture.Disciplines[discipline] = def;
            }

            if (def.Layers.Count == 0)
            {
                def.Layers = modules
                    .Select(m => m.Layer)
                    .Distinct()
                    .OrderBy(v => v)
                    .Select(v => new LayerDefinition { Level = v, Name = $"L{v}" })
                    .ToList();
            }
        }
    }

    private static T DeserializeOrDefault<T>(string json, T fallback)
    {
        try
        {
            var val = JsonSerializer.Deserialize<T>(json, ModuleManifestJsonOpts);
            return val == null ? fallback : val;
        }
        catch
        {
            return fallback;
        }
    }
}
