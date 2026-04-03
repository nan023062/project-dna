using System.Text.Json;
using System.Text.Json.Serialization;
using Dna.Knowledge.FileProtocol;
using Dna.Knowledge.Workspace.Models;
using Dna.Memory.Store;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dna.Knowledge;

/// <summary>
/// TopoGraph 持久化存储，负责图谱定义、模块注册、计算依赖和节点知识物化。
/// </summary>
public sealed class TopoGraphStore : ITopoGraphStore
{
    private readonly ILogger<TopoGraphStore> _logger;
    private readonly object _lock = new();
    private ArchitectureManifest _architecture = new();
    private ModulesManifest _manifest = new();
    private ComputedManifest _computed = new();
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
        LoadAllManifests();
        _initialized = true;
    }

    public void Reload()
    {
        if (!_initialized)
            return;

        LoadAllManifests();
    }

    public ArchitectureManifest GetArchitecture()
    {
        lock (_lock)
            return _architecture;
    }

    public ModulesManifest GetModulesManifest()
    {
        lock (_lock)
            return _manifest;
    }

    public ComputedManifest GetComputedManifest()
    {
        lock (_lock)
            return _computed;
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

        lock (_lock)
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
            cmd.Parameters.AddWithValue("@lessons", JsonSerializer.Serialize(knowledge.Lessons ?? [], JsonOpts));
            cmd.Parameters.AddWithValue("@tasks", JsonSerializer.Serialize(knowledge.ActiveTasks ?? [], JsonOpts));
            cmd.Parameters.AddWithValue("@facts", JsonSerializer.Serialize(knowledge.Facts ?? [], JsonOpts));
            cmd.Parameters.AddWithValue("@total", Math.Max(knowledge.TotalMemoryCount, 0));
            cmd.Parameters.AddWithValue("@memoryIds", JsonSerializer.Serialize(knowledge.MemoryIds ?? [], JsonOpts));
            cmd.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();
        }
    }

    public List<string> ResolveNodeIdCandidates(string? nodeId, bool strict = false)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
            return [];

        var input = nodeId.Trim();

        lock (_lock)
        {
            foreach (var modules in _manifest.Disciplines.Values)
            {
                var byId = modules.FirstOrDefault(m => string.Equals(m.Id, input, StringComparison.OrdinalIgnoreCase));
                if (byId != null)
                    return [byId.Id, byId.Name];
            }

            foreach (var modules in _manifest.Disciplines.Values)
            {
                var byName = modules.FirstOrDefault(m => string.Equals(m.Name, input, StringComparison.OrdinalIgnoreCase));
                if (byName != null)
                    return [byName.Id, byName.Name];
            }
        }

        if (strict)
            throw new InvalidOperationException($"nodeId '{input}' 不存在于已注册模块中。请传模块 Id 或 Name。");

        return [input];
    }

    public void UpdateComputedDependencies(string moduleName, List<string> computedDependencies)
    {
        lock (_lock)
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

    public void RegisterModule(string discipline, ModuleRegistration module)
    {
        if (string.IsNullOrWhiteSpace(module.Id))
            module.Id = NewId();

        lock (_lock)
        {
            discipline = NormalizeDisciplineId(discipline);
            NormalizeModuleRegistration(module);

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

            ValidateModuleHierarchyLocked(discipline, module);
            RemoveExistingModuleRegistrationLocked(module.Id, module.Name);
            modules.Add(module);

            SaveModulesManifestLocked();
            _logger.LogInformation("模块已注册: {Module} → {Discipline}", module.Name, discipline);
        }
    }

    public bool UnregisterModule(string name)
    {
        lock (_lock)
        {
            ModuleRegistration? removedModule = null;
            foreach (var (discipline, modules) in _manifest.Disciplines)
            {
                var removed = modules.RemoveAll(m =>
                {
                    var match = string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase);
                    if (match)
                        removedModule = m;
                    return match;
                });
                if (removed <= 0)
                    continue;

                if (!string.IsNullOrWhiteSpace(removedModule?.Id))
                    ClearParentReferencesLocked(removedModule.Id);

                SaveModulesManifestLocked();
                _logger.LogInformation("模块已注销: {Module} (from {Discipline})", name, discipline);
                return true;
            }

            return false;
        }
    }

    public void SaveCrossWork(CrossWorkRegistration crossWork)
    {
        lock (_lock)
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

    public bool RemoveCrossWork(string crossWorkId)
    {
        lock (_lock)
        {
            var removed = _manifest.CrossWorks.RemoveAll(cw =>
                string.Equals(cw.Id, crossWorkId, StringComparison.OrdinalIgnoreCase));
            if (removed > 0)
                SaveModulesManifestLocked();
            return removed > 0;
        }
    }

    public void UpsertDiscipline(string disciplineId, string? displayName, string roleId, List<LayerDefinition> layers)
    {
        if (string.IsNullOrWhiteSpace(disciplineId))
            throw new ArgumentException("disciplineId 不能为空");

        lock (_lock)
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
        lock (_lock)
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

    public void ReplaceModulesManifest(ModulesManifest manifest)
    {
        lock (_lock)
        {
            _manifest = manifest;
            SaveModulesManifestLocked();
            _logger.LogInformation("modules.json 已整体替换，模块数: {Count}",
                manifest.Disciplines.Values.Sum(d => d.Count));
        }
    }

    internal void RegisterFeature(string featureId, FeatureDefinition feature)
    {
        lock (_lock)
        {
            _manifest.Features[featureId] = feature;
            SaveModulesManifestLocked();
        }
    }

    private static string NormalizeDisciplineId(string discipline)
    {
        if (string.IsNullOrWhiteSpace(discipline))
            throw new InvalidOperationException("discipline 不能为空");

        return discipline.Trim().ToLowerInvariant();
    }

    private static void NormalizeModuleRegistration(ModuleRegistration module)
    {
        module.Name = module.Name?.Trim() ?? string.Empty;
        module.Path = NormalizeManagedPath(module.Path)
            ?? throw new InvalidOperationException("module.path 不能为空");
        module.ParentModuleId = NormalizeOptionalString(module.ParentModuleId);
        module.Maintainer = NormalizeOptionalString(module.Maintainer);
        module.Summary = NormalizeOptionalString(module.Summary);
        module.Boundary = NormalizeOptionalString(module.Boundary);

        module.Dependencies = NormalizeStringList(module.Dependencies);
        module.PublicApi = NormalizeNullableStringList(module.PublicApi);
        module.Constraints = NormalizeNullableStringList(module.Constraints);
        module.ManagedPaths = NormalizeNullableManagedPaths(module.ManagedPaths, module.Path);

        if (module.Metadata is { Count: > 0 })
        {
            module.Metadata = module.Metadata
                .Where(x => !string.IsNullOrWhiteSpace(x.Key) && !string.IsNullOrWhiteSpace(x.Value))
                .ToDictionary(x => x.Key.Trim(), x => x.Value.Trim(), StringComparer.OrdinalIgnoreCase);
            if (module.Metadata.Count == 0)
                module.Metadata = null;
        }
    }

    private void ValidateModuleHierarchyLocked(string discipline, ModuleRegistration module)
    {
        if (string.IsNullOrWhiteSpace(module.Name))
            throw new InvalidOperationException("module.name 不能为空");

        if (string.IsNullOrWhiteSpace(module.ParentModuleId))
            return;

        if (string.Equals(module.ParentModuleId, module.Id, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(module.ParentModuleId, module.Name, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("模块不能将自己设置为父模块");
        }

        if (!TryFindModuleLocked(module.ParentModuleId, out var parentDiscipline, out var parentModule))
            throw new InvalidOperationException($"未找到父模块：{module.ParentModuleId}");

        if (!string.Equals(parentDiscipline, discipline, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("MVP 阶段父子模块必须在同一部门内");

        var currentId = parentModule.ParentModuleId;
        var guard = 0;
        while (!string.IsNullOrWhiteSpace(currentId) && guard < 128)
        {
            if (string.Equals(currentId, module.Id, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(currentId, module.Name, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("模块父子关系不能形成循环");
            }

            if (!TryFindModuleLocked(currentId, out _, out var currentParent))
                break;

            currentId = currentParent.ParentModuleId;
            guard++;
        }
    }

    private void RemoveExistingModuleRegistrationLocked(string moduleId, string moduleName)
    {
        foreach (var modules in _manifest.Disciplines.Values)
        {
            modules.RemoveAll(existing =>
                (!string.IsNullOrWhiteSpace(moduleId) &&
                 string.Equals(existing.Id, moduleId, StringComparison.OrdinalIgnoreCase)) ||
                string.Equals(existing.Name, moduleName, StringComparison.OrdinalIgnoreCase));
        }
    }

    private void ClearParentReferencesLocked(string removedModuleId)
    {
        foreach (var modules in _manifest.Disciplines.Values)
        {
            foreach (var module in modules)
            {
                if (string.Equals(module.ParentModuleId, removedModuleId, StringComparison.OrdinalIgnoreCase))
                    module.ParentModuleId = null;
            }
        }
    }

    private bool TryFindModuleLocked(string? moduleIdOrName, out string discipline, out ModuleRegistration module)
    {
        discipline = string.Empty;
        module = default!;

        if (string.IsNullOrWhiteSpace(moduleIdOrName))
            return false;

        var normalized = moduleIdOrName.Trim();
        foreach (var (candidateDiscipline, modules) in _manifest.Disciplines)
        {
            var match = modules.FirstOrDefault(existing =>
                string.Equals(existing.Id, normalized, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(existing.Name, normalized, StringComparison.OrdinalIgnoreCase));
            if (match is null)
                continue;

            discipline = candidateDiscipline;
            module = match;
            return true;
        }

        return false;
    }

    private static string? NormalizeOptionalString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Trim();
    }

    private static List<string> NormalizeStringList(List<string>? values)
    {
        return (values ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string>? NormalizeNullableStringList(List<string>? values)
    {
        var normalized = NormalizeStringList(values);
        return normalized.Count == 0 ? null : normalized;
    }

    private static List<string>? NormalizeNullableManagedPaths(List<string>? values, string primaryPath)
    {
        var normalized = new List<string>();

        void AddValue(string? raw)
        {
            var path = NormalizeManagedPath(raw);
            if (string.IsNullOrWhiteSpace(path))
                return;

            if (!normalized.Contains(path, StringComparer.OrdinalIgnoreCase))
                normalized.Add(path);
        }

        AddValue(primaryPath);
        foreach (var value in values ?? [])
            AddValue(value);

        return normalized.Count == 0 ? null : normalized;
    }

    private static string? NormalizeManagedPath(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var normalized = raw.Replace('\\', '/').Trim().Trim('/');
        return normalized.Length == 0 ? null : normalized;
    }

    private void LoadAllManifests()
    {
        lock (_lock)
        {
            _graphDbPath = Path.Combine(_storePath, "graph.db");
            EnsureGraphSchemaLocked();
            LoadGraphSnapshotLocked();

            if (IsGraphSnapshotEmpty() && TryLoadLegacyJsonLocked())
            {
                SaveGraphSnapshotLocked();
                CleanupLegacyJsonFilesLocked();
            }

            // 文件协议回退：graph.db 和 legacy JSON 都为空时，从 .agentic-os/ 明文文件加载
            if (IsGraphSnapshotEmpty() && TryLoadFromFileProtocolLocked())
            {
                _logger.LogInformation("从 .agentic-os/ 明文文件加载图谱数据");
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
        if (!File.Exists(path))
            return null;

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(json, JsonOpts);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{File} 解析失败", Path.GetFileName(path));
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
                        def.Layers = JsonSerializer.Deserialize<List<LayerDefinition>>(layers.GetRawText(), JsonOpts) ?? [];

                    if (prop.Value.TryGetProperty("modules", out var mods))
                        modules = JsonSerializer.Deserialize<List<ModuleRegistration>>(mods.GetRawText(), JsonOpts) ?? [];

                    _architecture.Disciplines[id] = def;
                    _manifest.Disciplines[id] = modules;
                }
            }

            if (root.TryGetProperty("crossWorks", out var cw))
                _manifest.CrossWorks = JsonSerializer.Deserialize<List<CrossWorkRegistration>>(cw.GetRawText(), JsonOpts) ?? [];

            if (root.TryGetProperty("features", out var ft))
                _manifest.Features = JsonSerializer.Deserialize<Dictionary<string, FeatureDefinition>>(ft.GetRawText(), JsonOpts) ?? new();

            _logger.LogInformation("已解析旧格式清单: {Path}", legacyPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "旧格式迁移失败");
        }
    }

    private void SaveArchitectureLocked() => SaveGraphSnapshotLocked();
    private void SaveModulesManifestLocked() => SaveGraphSnapshotLocked();
    private void SaveComputedManifestLocked() => SaveGraphSnapshotLocked();

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
                parent_module_id TEXT,
                is_crosswork_module INTEGER NOT NULL DEFAULT 0,
                participants_json TEXT NOT NULL DEFAULT '[]',
                dependencies_json TEXT NOT NULL DEFAULT '[]',
                managed_paths_json TEXT,
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
        EnsureColumnExists(conn, "graph_modules", "parent_module_id", "TEXT");
        EnsureColumnExists(conn, "graph_modules", "managed_paths_json", "TEXT");
    }

    private static void EnsureColumnExists(SqliteConnection conn, string tableName, string columnName, string definition)
    {
        using var pragma = conn.CreateCommand();
        pragma.CommandText = $"PRAGMA table_info({tableName})";
        using var reader = pragma.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                return;
        }

        using var alter = conn.CreateCommand();
        alter.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {definition}";
        alter.ExecuteNonQuery();
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
                SELECT id, name, discipline, path, layer, parent_module_id, is_crosswork_module,
                       participants_json, dependencies_json, managed_paths_json, maintainer, summary,
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
                    ParentModuleId = reader.IsDBNull(5) ? null : reader.GetString(5),
                    IsCrossWorkModule = reader.GetInt64(6) == 1,
                    Participants = DeserializeOrDefault(reader.IsDBNull(7) ? "[]" : reader.GetString(7), new List<CrossWorkParticipantRegistration>()),
                    Dependencies = DeserializeOrDefault(reader.IsDBNull(8) ? "[]" : reader.GetString(8), new List<string>()),
                    ManagedPaths = reader.IsDBNull(9) ? null : DeserializeOrDefault<List<string>>(reader.GetString(9), []),
                    Maintainer = reader.IsDBNull(10) ? null : reader.GetString(10),
                    Summary = reader.IsDBNull(11) ? null : reader.GetString(11),
                    Boundary = reader.IsDBNull(12) ? null : reader.GetString(12),
                    PublicApi = reader.IsDBNull(13) ? null : DeserializeOrDefault<List<string>>(reader.GetString(13), []),
                    Constraints = reader.IsDBNull(14) ? null : DeserializeOrDefault<List<string>>(reader.GetString(14), []),
                    Metadata = reader.IsDBNull(15) ? null : DeserializeOrDefault<Dictionary<string, string>>(reader.GetString(15), new Dictionary<string, string>())
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
            cmd.Parameters.AddWithValue("@layers", JsonSerializer.Serialize(def.Layers ?? [], JsonOpts));
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
                        (id, name, discipline, path, layer, parent_module_id, is_crosswork_module,
                         participants_json, dependencies_json, managed_paths_json, maintainer, summary,
                         boundary, public_api_json, constraints_json, metadata_json)
                    VALUES
                        (@id, @name, @discipline, @path, @layer, @parentModuleId, @isCw,
                         @participants, @dependencies, @managedPaths, @maintainer, @summary,
                         @boundary, @publicApi, @constraints, @metadata)
                    """;
                cmd.Parameters.AddWithValue("@id", module.Id);
                cmd.Parameters.AddWithValue("@name", module.Name);
                cmd.Parameters.AddWithValue("@discipline", discipline);
                cmd.Parameters.AddWithValue("@path", module.Path);
                cmd.Parameters.AddWithValue("@layer", module.Layer);
                cmd.Parameters.AddWithValue("@parentModuleId", (object?)module.ParentModuleId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@isCw", module.IsCrossWorkModule ? 1 : 0);
                cmd.Parameters.AddWithValue("@participants", JsonSerializer.Serialize(module.Participants ?? [], JsonOpts));
                cmd.Parameters.AddWithValue("@dependencies", JsonSerializer.Serialize(module.Dependencies ?? [], JsonOpts));
                cmd.Parameters.AddWithValue("@managedPaths", module.ManagedPaths == null ? DBNull.Value : JsonSerializer.Serialize(module.ManagedPaths, JsonOpts));
                cmd.Parameters.AddWithValue("@maintainer", (object?)module.Maintainer ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@summary", (object?)module.Summary ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@boundary", (object?)module.Boundary ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@publicApi", module.PublicApi == null ? DBNull.Value : JsonSerializer.Serialize(module.PublicApi, JsonOpts));
                cmd.Parameters.AddWithValue("@constraints", module.Constraints == null ? DBNull.Value : JsonSerializer.Serialize(module.Constraints, JsonOpts));
                cmd.Parameters.AddWithValue("@metadata", module.Metadata == null ? DBNull.Value : JsonSerializer.Serialize(module.Metadata, JsonOpts));
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
            cmd.Parameters.AddWithValue("@participants", JsonSerializer.Serialize(cw.Participants ?? [], JsonOpts));
            cmd.ExecuteNonQuery();
        }

        foreach (var (id, feature) in _manifest.Features)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO graph_features (id, payload_json) VALUES (@id, @payload)";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@payload", JsonSerializer.Serialize(feature, JsonOpts));
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
            cmd.Parameters.AddWithValue("@deps", JsonSerializer.Serialize(deps ?? [], JsonOpts));
            cmd.ExecuteNonQuery();
        }

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
        var archPath = Path.Combine(_storePath, "architecture.json");
        var modulesPath = Path.Combine(_storePath, "modules.json");
        var computedPath = Path.Combine(_storePath, "modules.computed.json");
        var legacyManifestPath = Path.Combine(_storePath, "modules.manifest.json");

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
            var value = JsonSerializer.Deserialize<T>(json, JsonOpts);
            return value == null ? fallback : value;
        }
        catch
        {
            return fallback;
        }
    }

    private static string NewId() => UlidGenerator.New();

    /// <summary>
    /// 从 .agentic-os/ 明文文件协议加载图谱数据。
    /// 当 graph.db 和 legacy JSON 都为空时作为回退方案。
    /// </summary>
    private bool TryLoadFromFileProtocolLocked()
    {
        try
        {
            // _storePath 通常指向 .agentic-os/knowledge/ 或 .agentic-os/
            // 需要找到包含 knowledge/modules/ 的 .agentic-os/ 路径
            var agenticOsPath = ResolveAgenticOsPathFromStore(_storePath);
            if (string.IsNullOrEmpty(agenticOsPath))
                return false;

            var modulesRoot = FileProtocolPaths.GetModulesRoot(agenticOsPath);
            if (!Directory.Exists(modulesRoot))
                return false;

            var (architecture, modules) = FileProtocolLegacyAdapter.LoadAsLegacyManifests(agenticOsPath);

            if (modules.Disciplines.Count == 0)
                return false;

            _architecture = architecture;
            _manifest = modules;
            _computed = new ComputedManifest();

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "从 .agentic-os/ 文件协议加载图谱失败");
            return false;
        }
    }

    private static string? ResolveAgenticOsPathFromStore(string storePath)
    {
        if (string.IsNullOrWhiteSpace(storePath))
            return null;

        // storePath 可能是 .agentic-os/knowledge/ 或 .agentic-os/ 或项目根目录
        var candidates = new[]
        {
            // storePath 本身是 .agentic-os/
            storePath,
            // storePath 是 .agentic-os/knowledge/ → 上一级
            Path.GetDirectoryName(storePath),
            // storePath 是项目根目录 → 子目录 .agentic-os/
            Path.Combine(storePath, FileProtocolPaths.AgenticOsDir),
        };

        foreach (var candidate in candidates)
        {
            if (candidate == null) continue;
            var modulesDir = FileProtocolPaths.GetModulesRoot(candidate);
            if (Directory.Exists(modulesDir))
                return candidate;
        }

        return null;
    }
}
