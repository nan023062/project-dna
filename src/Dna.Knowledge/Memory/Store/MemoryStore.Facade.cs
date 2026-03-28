using Dna.Core.Config;
using Dna.Knowledge.Models;
using Dna.Knowledge.Project.Models;
using Dna.Memory.Models;
using Dna.Memory.Services;
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

    public void RegisterModule(string discipline, ModuleRegistration module)
    {
        if (string.IsNullOrWhiteSpace(module.Id))
            module.Id = UlidGenerator.New();

        lock (_manifestLock)
        {
            if (!_architecture.Disciplines.ContainsKey(discipline))
                throw new InvalidOperationException(
                    $"Discipline '{discipline}' 不存在于 architecture.json 中。");

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

    // ═══════════════════════════════════════════

    /// <summary>生成一个新的 ULID（时间有序全局唯一 ID）</summary>
    public static string NewId() => UlidGenerator.New();

    private MemoryWriter Writer => _writer ?? throw new InvalidOperationException("MemoryStore not initialized. Call BuildInternals first.");
    private MemoryReader Reader => _reader ?? throw new InvalidOperationException("MemoryStore not initialized. Call BuildInternals first.");
    private MemoryRecallEngine RecallEngine => _recallEngine ?? throw new InvalidOperationException("MemoryStore not initialized. Call BuildInternals first.");

    internal void LoadAllManifests()
    {
        var baseDir = Path.Combine(_projectRoot, ".dna");
        var archPath = Path.Combine(baseDir, "architecture.json");
        var modulesPath = Path.Combine(baseDir, "modules.json");
        var computedPath = Path.Combine(baseDir, "modules.computed.json");

        lock (_manifestLock)
        {
            _architecture = LoadJsonFile<ArchitectureManifest>(archPath) ?? new ArchitectureManifest();
            _computed = LoadJsonFile<ComputedManifest>(computedPath) ?? new ComputedManifest();

            if (_architecture.Disciplines.Count == 0)
            {
                var legacyManifest = Path.Combine(baseDir, "modules.manifest.json");
                if (File.Exists(legacyManifest))
                {
                    MigrateLegacy(legacyManifest);
                }
                else if (File.Exists(modulesPath))
                {
                    MigrateLegacy(modulesPath);
                }
                else
                {
                    _manifest = new ModulesManifest();
                }
            }
            else
            {
                _manifest = LoadJsonFile<ModulesManifest>(modulesPath) ?? new ModulesManifest();
            }

            _logger.LogInformation(
                "清单加载完成: 部门={Depts}, 模块={Mods}, 计算依赖={Facts}",
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

            SaveArchitectureLocked();
            SaveModulesManifestLocked();
            File.Delete(legacyPath);
            _logger.LogInformation("已从旧格式迁移至 architecture.json + modules.json");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "旧格式迁移失败");
        }
    }

    private void SaveArchitectureLocked()
    {
        var path = Path.Combine(_projectRoot, ".dna", "architecture.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(_architecture, ModuleManifestJsonOpts));
    }

    private void SaveModulesManifestLocked()
    {
        var path = Path.Combine(_projectRoot, ".dna", "modules.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(_manifest, ModuleManifestJsonOpts));
    }

    private void SaveComputedManifestLocked()
    {
        var path = Path.Combine(_projectRoot, ".dna", "modules.computed.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(_computed, ModuleManifestJsonOpts));
    }
}
