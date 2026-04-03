using System.Text.Json;
using System.Text.Json.Serialization;
using Dna.Knowledge.FileProtocol;
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
        _graphDbPath = Path.Combine(storePath, TopoGraphConstants.Storage.GraphDbFileName);
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
            var map = new Dictionary<string, NodeKnowledge>(StringComparer.OrdinalIgnoreCase);
            var agenticOsPath = ResolveAgenticOsPathFromStore(_storePath);
            if (string.IsNullOrWhiteSpace(agenticOsPath))
                return map;

            foreach (var module in _fileStore.LoadModules(agenticOsPath))
            {
                var identityMarkdown = _fileStore.LoadIdentity(agenticOsPath, module.Uid);
                if (string.IsNullOrWhiteSpace(identityMarkdown))
                    continue;

                map[module.Uid] = ParseNodeKnowledgeMarkdown(identityMarkdown);
            }

            return map;
        }
    }

    public void UpsertNodeKnowledge(string nodeId, NodeKnowledge knowledge)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
            throw new ArgumentException("nodeId 不能为空", nameof(nodeId));

        ArgumentNullException.ThrowIfNull(knowledge);

        lock (_lock)
            SyncNodeKnowledgeToFileProtocolLocked(nodeId, knowledge);
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
            throw new InvalidOperationException(string.Format(TopoGraphConstants.Context.MissingModuleTemplate, input));

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
                delete.CommandText = $"DELETE FROM {TopoGraphConstants.Storage.ComputedDependenciesTable} WHERE module_name = @module";
                delete.Parameters.AddWithValue("@module", moduleName);
                delete.ExecuteNonQuery();
            }

            using (var insert = conn.CreateCommand())
            {
                insert.Transaction = tx;
                insert.CommandText = $"""
                    INSERT INTO {TopoGraphConstants.Storage.ComputedDependenciesTable} (module_name, dependencies_json)
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
        cmd.CommandText = $"SELECT module_name, dependencies_json FROM {TopoGraphConstants.Storage.ComputedDependenciesTable}";
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
        cmd.CommandText = $"""
            CREATE TABLE IF NOT EXISTS {TopoGraphConstants.Storage.ComputedDependenciesTable} (
                module_name TEXT PRIMARY KEY,
                dependencies_json TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
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
        {
            _logger.LogWarning("[TopoGraphStore] Cannot persist node knowledge because .agentic-os path was not resolved. NodeId={NodeId}", nodeId);
            return;
        }

        var module = _fileStore.LoadModule(agenticOsPath, nodeId);
        if (module == null)
        {
            _logger.LogWarning("[TopoGraphStore] Cannot persist node knowledge because module file was not found. NodeId={NodeId}", nodeId);
            return;
        }

        var dependencies = _fileStore.LoadDependencies(agenticOsPath, nodeId);
        _fileStore.SaveModule(agenticOsPath, module, BuildIdentityMarkdown(knowledge), dependencies);
    }

    private static NodeKnowledge ParseNodeKnowledgeMarkdown(string markdown)
    {
        var sections = ParseMarkdownSections(markdown);
        ParseGovernanceSection(
            GetSectionLines(sections, TopoGraphConstants.Storage.KnowledgeDocument.GovernanceHeading),
            out var identityMemoryId,
            out var upgradeTrailMemoryId,
            out var totalMemoryCount,
            out var memoryIds);

        return new NodeKnowledge
        {
            Identity = ParseSummary(GetSectionLines(sections, TopoGraphConstants.Storage.KnowledgeDocument.SummaryHeading)),
            Lessons = ParseLessons(GetSectionLines(sections, TopoGraphConstants.Storage.KnowledgeDocument.LessonsHeading)),
            ActiveTasks = ParseBulletList(GetSectionLines(sections, TopoGraphConstants.Storage.KnowledgeDocument.ActiveTasksHeading)),
            Facts = ParseBulletList(GetSectionLines(sections, TopoGraphConstants.Storage.KnowledgeDocument.FactsHeading)),
            TotalMemoryCount = totalMemoryCount,
            IdentityMemoryId = identityMemoryId,
            UpgradeTrailMemoryId = upgradeTrailMemoryId,
            MemoryIds = memoryIds
        };
    }

    private static Dictionary<string, List<string>> ParseMarkdownSections(string markdown)
    {
        var sections = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        string? currentHeading = null;

        foreach (var rawLine in NormalizeMarkdown(markdown).Split('\n'))
        {
            var line = rawLine.TrimEnd();
            if (line.StartsWith(TopoGraphConstants.Storage.KnowledgeDocument.SecondLevelHeadingPrefix, StringComparison.Ordinal))
            {
                currentHeading = line[TopoGraphConstants.Storage.KnowledgeDocument.SecondLevelHeadingPrefix.Length..].Trim();
                if (!sections.ContainsKey(currentHeading))
                    sections[currentHeading] = [];
                continue;
            }

            if (currentHeading != null)
                sections[currentHeading].Add(line);
        }

        return sections;
    }

    private static List<string> GetSectionLines(Dictionary<string, List<string>> sections, string heading)
        => sections.TryGetValue(heading, out var lines) ? lines : [];

    private static string? ParseSummary(List<string> lines)
    {
        var summary = string.Join('\n', lines).Trim();
        if (string.IsNullOrWhiteSpace(summary) ||
            string.Equals(summary, TopoGraphConstants.Storage.KnowledgeDocument.EmptySummaryFallback, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return summary;
    }

    private static List<LessonSummary> ParseLessons(List<string> lines)
    {
        var lessons = new List<LessonSummary>();
        foreach (var bullet in ParseBulletList(lines))
        {
            var separatorIndex = bullet.LastIndexOf(TopoGraphConstants.Storage.KnowledgeDocument.LessonResolutionSeparator, StringComparison.Ordinal);
            if (separatorIndex > 0 && separatorIndex < bullet.Length - TopoGraphConstants.Storage.KnowledgeDocument.LessonResolutionSeparator.Length)
            {
                lessons.Add(new LessonSummary
                {
                    Title = bullet[..separatorIndex].Trim(),
                    Resolution = bullet[(separatorIndex + TopoGraphConstants.Storage.KnowledgeDocument.LessonResolutionSeparator.Length)..].Trim()
                });
            }
            else
            {
                lessons.Add(new LessonSummary
                {
                    Title = bullet.Trim()
                });
            }
        }

        return lessons.Where(item => !string.IsNullOrWhiteSpace(item.Title)).ToList();
    }

    private static List<string> ParseBulletList(List<string> lines)
    {
        return lines
            .Select(line => line.Trim())
            .Where(line => line.StartsWith(TopoGraphConstants.Storage.KnowledgeDocument.BulletPrefix, StringComparison.Ordinal))
            .Select(line => line[TopoGraphConstants.Storage.KnowledgeDocument.BulletPrefix.Length..].Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();
    }

    private static void ParseGovernanceSection(
        List<string> lines,
        out string? identityMemoryId,
        out string? upgradeTrailMemoryId,
        out int totalMemoryCount,
        out List<string> memoryIds)
    {
        identityMemoryId = null;
        upgradeTrailMemoryId = null;
        totalMemoryCount = 0;
        memoryIds = [];
        var inReferencedMemories = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (line.StartsWith(TopoGraphConstants.Storage.KnowledgeDocument.IdentityMemoryPrefix, StringComparison.OrdinalIgnoreCase))
            {
                identityMemoryId = ExtractGovernanceValue(line, TopoGraphConstants.Storage.KnowledgeDocument.IdentityMemoryPrefix);
                inReferencedMemories = false;
                continue;
            }

            if (line.StartsWith(TopoGraphConstants.Storage.KnowledgeDocument.UpgradeTrailPrefix, StringComparison.OrdinalIgnoreCase))
            {
                upgradeTrailMemoryId = ExtractGovernanceValue(line, TopoGraphConstants.Storage.KnowledgeDocument.UpgradeTrailPrefix);
                inReferencedMemories = false;
                continue;
            }

            if (line.StartsWith(TopoGraphConstants.Storage.KnowledgeDocument.SourceCountPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var value = ExtractGovernanceValue(line, TopoGraphConstants.Storage.KnowledgeDocument.SourceCountPrefix);
                if (!string.IsNullOrWhiteSpace(value) && int.TryParse(value, out var parsedCount))
                    totalMemoryCount = Math.Max(parsedCount, 0);
                inReferencedMemories = false;
                continue;
            }

            if (line.StartsWith(TopoGraphConstants.Storage.KnowledgeDocument.ReferencedMemoriesPrefix, StringComparison.OrdinalIgnoreCase))
            {
                inReferencedMemories = true;
                continue;
            }

            if (inReferencedMemories && line.StartsWith(TopoGraphConstants.Storage.KnowledgeDocument.BulletPrefix, StringComparison.Ordinal))
            {
                var memoryId = line[TopoGraphConstants.Storage.KnowledgeDocument.BulletPrefix.Length..].Trim();
                if (memoryId.StartsWith(TopoGraphConstants.Storage.KnowledgeDocument.TruncatedListPrefix, StringComparison.Ordinal))
                    continue;

                memoryId = TrimMarkdownCode(memoryId);
                if (!string.IsNullOrWhiteSpace(memoryId))
                    memoryIds.Add(memoryId);
                continue;
            }

            inReferencedMemories = false;
        }
    }

    private static string? ExtractGovernanceValue(string line, string prefix)
    {
        var value = line[prefix.Length..].Trim();
        value = TrimMarkdownCode(value);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string TrimMarkdownCode(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length >= 2 &&
               trimmed.StartsWith('`') &&
               trimmed.EndsWith('`')
            ? trimmed[1..^1].Trim()
            : trimmed;
    }

    private static string NormalizeMarkdown(string markdown)
        => markdown.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

    private static string BuildIdentityMarkdown(NodeKnowledge knowledge)
    {
        var lines = new List<string>
        {
            $"{TopoGraphConstants.Storage.KnowledgeDocument.SecondLevelHeadingPrefix}{TopoGraphConstants.Storage.KnowledgeDocument.SummaryHeading}",
            string.Empty,
            string.IsNullOrWhiteSpace(knowledge.Identity)
                ? TopoGraphConstants.Storage.KnowledgeDocument.EmptySummaryFallback
                : knowledge.Identity.Trim()
        };

        if (knowledge.Lessons.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add($"{TopoGraphConstants.Storage.KnowledgeDocument.SecondLevelHeadingPrefix}{TopoGraphConstants.Storage.KnowledgeDocument.LessonsHeading}");
            lines.Add(string.Empty);
            foreach (var lesson in knowledge.Lessons.Where(item => !string.IsNullOrWhiteSpace(item.Title)))
            {
                var suffix = string.IsNullOrWhiteSpace(lesson.Resolution)
                    ? string.Empty
                    : $"{TopoGraphConstants.Storage.KnowledgeDocument.LessonResolutionSeparator}{lesson.Resolution!.Trim()}";
                lines.Add($"{TopoGraphConstants.Storage.KnowledgeDocument.BulletPrefix}{lesson.Title.Trim()}{suffix}");
            }
        }

        if (knowledge.ActiveTasks.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add($"{TopoGraphConstants.Storage.KnowledgeDocument.SecondLevelHeadingPrefix}{TopoGraphConstants.Storage.KnowledgeDocument.ActiveTasksHeading}");
            lines.Add(string.Empty);
            foreach (var task in knowledge.ActiveTasks.Where(item => !string.IsNullOrWhiteSpace(item)))
                lines.Add($"{TopoGraphConstants.Storage.KnowledgeDocument.BulletPrefix}{task.Trim()}");
        }

        if (knowledge.Facts.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add($"{TopoGraphConstants.Storage.KnowledgeDocument.SecondLevelHeadingPrefix}{TopoGraphConstants.Storage.KnowledgeDocument.FactsHeading}");
            lines.Add(string.Empty);
            foreach (var fact in knowledge.Facts.Where(item => !string.IsNullOrWhiteSpace(item)))
                lines.Add($"{TopoGraphConstants.Storage.KnowledgeDocument.BulletPrefix}{fact.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(knowledge.IdentityMemoryId) ||
            !string.IsNullOrWhiteSpace(knowledge.UpgradeTrailMemoryId) ||
            knowledge.TotalMemoryCount > 0 ||
            knowledge.MemoryIds.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add($"{TopoGraphConstants.Storage.KnowledgeDocument.SecondLevelHeadingPrefix}{TopoGraphConstants.Storage.KnowledgeDocument.GovernanceHeading}");
            lines.Add(string.Empty);

            if (!string.IsNullOrWhiteSpace(knowledge.IdentityMemoryId))
                lines.Add($"{TopoGraphConstants.Storage.KnowledgeDocument.IdentityMemoryPrefix} `{knowledge.IdentityMemoryId!.Trim()}`");

            if (!string.IsNullOrWhiteSpace(knowledge.UpgradeTrailMemoryId))
                lines.Add($"{TopoGraphConstants.Storage.KnowledgeDocument.UpgradeTrailPrefix} `{knowledge.UpgradeTrailMemoryId!.Trim()}`");

            if (knowledge.TotalMemoryCount > 0)
                lines.Add($"{TopoGraphConstants.Storage.KnowledgeDocument.SourceCountPrefix} {knowledge.TotalMemoryCount}");

            if (knowledge.MemoryIds.Count > 0)
            {
                lines.Add(TopoGraphConstants.Storage.KnowledgeDocument.ReferencedMemoriesPrefix);
                foreach (var memoryId in knowledge.MemoryIds.Take(TopoGraphConstants.Storage.KnowledgeDocument.ReferencedMemoryPreviewLimit))
                    lines.Add($"  {TopoGraphConstants.Storage.KnowledgeDocument.BulletPrefix}`{memoryId}`");

                if (knowledge.MemoryIds.Count > TopoGraphConstants.Storage.KnowledgeDocument.ReferencedMemoryPreviewLimit)
                {
                    lines.Add(
                        $"  {TopoGraphConstants.Storage.KnowledgeDocument.BulletPrefix}{TopoGraphConstants.Storage.KnowledgeDocument.TruncatedListPrefix} (+{knowledge.MemoryIds.Count - TopoGraphConstants.Storage.KnowledgeDocument.ReferencedMemoryPreviewLimit} more)");
                }
            }
        }

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }
}
