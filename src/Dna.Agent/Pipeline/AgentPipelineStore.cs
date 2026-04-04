using System.Text.Json;
using Dna.Core.Config;

namespace Dna.Agent.Pipeline;

public sealed class AgentPipelineStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly object _sync = new();
    private readonly ProjectConfig _projectConfig;
    private AgentExecutionPipelineConfig? _cachedConfig;
    private PipelineRunResult? _latestRun;

    public AgentPipelineStore(ProjectConfig projectConfig)
    {
        _projectConfig = projectConfig;
    }

    public AgentExecutionPipelineConfig GetConfig()
    {
        lock (_sync)
        {
            if (_cachedConfig is not null)
                return Clone(_cachedConfig);

            var path = ResolveConfigPath();
            if (!File.Exists(path))
            {
                _cachedConfig = BuildDefaultConfig();
                SaveConfigLocked(_cachedConfig);
                return Clone(_cachedConfig);
            }

            try
            {
                var json = File.ReadAllText(path);
                _cachedConfig = JsonSerializer.Deserialize<AgentExecutionPipelineConfig>(json, JsonOptions)
                                ?? BuildDefaultConfig();
            }
            catch
            {
                _cachedConfig = BuildDefaultConfig();
            }

            Normalize(_cachedConfig);
            return Clone(_cachedConfig);
        }
    }

    public AgentExecutionPipelineConfig UpdateConfig(AgentExecutionPipelineConfig config)
    {
        lock (_sync)
        {
            Normalize(config);
            _cachedConfig = Clone(config);
            SaveConfigLocked(_cachedConfig);
            return Clone(_cachedConfig);
        }
    }

    public PipelineRunResult SaveLatestRun(PipelineRunResult result)
    {
        lock (_sync)
        {
            _latestRun = Clone(result);
            var json = JsonSerializer.Serialize(_latestRun, JsonOptions);
            File.WriteAllText(ResolveLatestRunPath(), json);
            return Clone(_latestRun);
        }
    }

    public PipelineRunResult? GetLatestRun()
    {
        lock (_sync)
        {
            if (_latestRun is not null)
                return Clone(_latestRun);

            var path = ResolveLatestRunPath();
            if (!File.Exists(path))
                return null;

            try
            {
                var json = File.ReadAllText(path);
                _latestRun = JsonSerializer.Deserialize<PipelineRunResult>(json, JsonOptions);
                return _latestRun is null ? null : Clone(_latestRun);
            }
            catch
            {
                return null;
            }
        }
    }

    private void SaveConfigLocked(AgentExecutionPipelineConfig config)
    {
        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(ResolveConfigPath(), json);
    }

    private string ResolveConfigPath()
        => Path.Combine(ResolveStoreDirectory(), AgentPipelineConstants.ConfigFileName);

    private string ResolveLatestRunPath()
        => Path.Combine(ResolveStoreDirectory(), AgentPipelineConstants.LatestRunFileName);

    private string ResolveStoreDirectory()
    {
        var root = _projectConfig.HasStore && !string.IsNullOrWhiteSpace(_projectConfig.MetadataRootPath)
            ? _projectConfig.MetadataRootPath
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".agentic-os");

        var path = Path.Combine(Path.GetFullPath(root), AgentPipelineConstants.DirectoryName);
        Directory.CreateDirectory(path);
        return path;
    }

    private static AgentExecutionPipelineConfig BuildDefaultConfig()
    {
        var config = new AgentExecutionPipelineConfig();
        Normalize(config);
        return config;
    }

    private static void Normalize(AgentExecutionPipelineConfig config)
    {
        config.ExecutionOrder = NormalizeOrder(config.ExecutionOrder);
        config.Slots ??= new Dictionary<string, AgentSlotConfig>(StringComparer.OrdinalIgnoreCase);

        if (!config.Slots.ContainsKey(AgentPipelineConstants.SlotIds.Architect))
            config.Slots[AgentPipelineConstants.SlotIds.Architect] = AgentSlotConfig.CreateArchitectDefault();

        if (!config.Slots.ContainsKey(AgentPipelineConstants.SlotIds.Developer))
            config.Slots[AgentPipelineConstants.SlotIds.Developer] = AgentSlotConfig.CreateDeveloperDefault();

        foreach (var (key, slot) in config.Slots.ToList())
        {
            slot.Id = string.IsNullOrWhiteSpace(slot.Id) ? key : slot.Id.Trim().ToLowerInvariant();
            slot.DisplayName = string.IsNullOrWhiteSpace(slot.DisplayName)
                ? slot.Id switch
                {
                    AgentPipelineConstants.SlotIds.Architect => AgentPipelineConstants.SlotDisplayNames.Architect,
                    AgentPipelineConstants.SlotIds.Developer => AgentPipelineConstants.SlotDisplayNames.Developer,
                    _ => slot.Id
                }
                : slot.DisplayName.Trim();

            slot.DefaultModules = slot.DefaultModules
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            slot.RecallQuestions = slot.RecallQuestions
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value.Trim())
                .ToList();
            slot.Objectives = slot.Objectives
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value.Trim())
                .ToList();

            if (slot.RecallQuestions.Count == 0)
            {
                slot.RecallQuestions = slot.Id == AgentPipelineConstants.SlotIds.Architect
                    ? AgentSlotConfig.CreateArchitectDefault().RecallQuestions
                    : AgentSlotConfig.CreateDeveloperDefault().RecallQuestions;
            }
        }
    }

    private static List<string> NormalizeOrder(IEnumerable<string>? order)
    {
        var normalized = (order ?? [])
            .Select(static value => (value ?? string.Empty).Trim().ToLowerInvariant())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!normalized.Contains(AgentPipelineConstants.SlotIds.Architect))
            normalized.Insert(0, AgentPipelineConstants.SlotIds.Architect);
        if (!normalized.Contains(AgentPipelineConstants.SlotIds.Developer))
            normalized.Add(AgentPipelineConstants.SlotIds.Developer);

        var architectIndex = normalized.FindIndex(value => value == AgentPipelineConstants.SlotIds.Architect);
        var developerIndex = normalized.FindIndex(value => value == AgentPipelineConstants.SlotIds.Developer);
        if (architectIndex > developerIndex)
        {
            normalized.RemoveAt(architectIndex);
            normalized.Insert(developerIndex, AgentPipelineConstants.SlotIds.Architect);
        }

        return normalized;
    }

    private static T Clone<T>(T source)
    {
        var json = JsonSerializer.Serialize(source, JsonOptions);
        return JsonSerializer.Deserialize<T>(json, JsonOptions)
               ?? throw new InvalidOperationException("Failed to clone pipeline state.");
    }
}
