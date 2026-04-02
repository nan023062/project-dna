using System.Text.Json;

namespace Dna.App.Services.Pipeline;

public sealed class AppPipelineStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly object _sync = new();
    private readonly string _configPath;
    private readonly string _latestRunPath;
    private AppExecutionPipelineConfig? _cachedConfig;
    private PipelineRunResult? _latestRun;

    public AppPipelineStore()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var dir = Path.Combine(home, ".dna");
        Directory.CreateDirectory(dir);

        _configPath = Path.Combine(dir, "app-pipeline.json");
        _latestRunPath = Path.Combine(dir, "app-pipeline-last-run.json");
    }

    public AppExecutionPipelineConfig GetConfig()
    {
        lock (_sync)
        {
            if (_cachedConfig != null) return Clone(_cachedConfig);

            if (!File.Exists(_configPath))
            {
                _cachedConfig = BuildDefaultConfig();
                SaveConfigLocked(_cachedConfig);
                return Clone(_cachedConfig);
            }

            try
            {
                var json = File.ReadAllText(_configPath);
                _cachedConfig = JsonSerializer.Deserialize<AppExecutionPipelineConfig>(json, JsonOptions)
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

    public AppExecutionPipelineConfig UpdateConfig(AppExecutionPipelineConfig config)
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
            File.WriteAllText(_latestRunPath, json);
            return Clone(_latestRun);
        }
    }

    public PipelineRunResult? GetLatestRun()
    {
        lock (_sync)
        {
            if (_latestRun != null) return Clone(_latestRun);
            if (!File.Exists(_latestRunPath)) return null;

            try
            {
                var json = File.ReadAllText(_latestRunPath);
                _latestRun = JsonSerializer.Deserialize<PipelineRunResult>(json, JsonOptions);
                return _latestRun == null ? null : Clone(_latestRun);
            }
            catch
            {
                return null;
            }
        }
    }

    private void SaveConfigLocked(AppExecutionPipelineConfig config)
    {
        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(_configPath, json);
    }

    private static AppExecutionPipelineConfig BuildDefaultConfig()
    {
        var config = new AppExecutionPipelineConfig();
        Normalize(config);
        return config;
    }

    private static void Normalize(AppExecutionPipelineConfig config)
    {
        config.ExecutionOrder = NormalizeOrder(config.ExecutionOrder);
        config.Slots ??= new Dictionary<string, AgentSlotConfig>(StringComparer.OrdinalIgnoreCase);

        if (!config.Slots.ContainsKey("architect"))
            config.Slots["architect"] = AgentSlotConfig.CreateArchitectDefault();
        if (!config.Slots.ContainsKey("developer"))
            config.Slots["developer"] = AgentSlotConfig.CreateDeveloperDefault();

        foreach (var (key, slot) in config.Slots.ToList())
        {
            slot.Id = string.IsNullOrWhiteSpace(slot.Id) ? key : slot.Id.Trim().ToLowerInvariant();
            slot.DisplayName = string.IsNullOrWhiteSpace(slot.DisplayName)
                ? (slot.Id == "architect" ? "架构师" : slot.Id == "developer" ? "开发者" : slot.Id)
                : slot.DisplayName.Trim();
            slot.DefaultModules = slot.DefaultModules?.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? [];
            slot.RecallQuestions = slot.RecallQuestions?.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList() ?? [];
            slot.Objectives = slot.Objectives?.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList() ?? [];
            if (slot.RecallQuestions.Count == 0)
            {
                slot.RecallQuestions = slot.Id == "architect"
                    ? AgentSlotConfig.CreateArchitectDefault().RecallQuestions
                    : AgentSlotConfig.CreateDeveloperDefault().RecallQuestions;
            }
        }
    }

    private static List<string> NormalizeOrder(IEnumerable<string>? order)
    {
        var normalized = (order ?? [])
            .Select(x => (x ?? string.Empty).Trim().ToLowerInvariant())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!normalized.Contains("architect")) normalized.Insert(0, "architect");
        if (!normalized.Contains("developer")) normalized.Add("developer");

        var architectIndex = normalized.FindIndex(x => x == "architect");
        var developerIndex = normalized.FindIndex(x => x == "developer");
        if (architectIndex > developerIndex)
        {
            normalized.RemoveAt(architectIndex);
            normalized.Insert(developerIndex, "architect");
        }

        return normalized;
    }

    private static T Clone<T>(T source)
    {
        var json = JsonSerializer.Serialize(source, JsonOptions);
        return JsonSerializer.Deserialize<T>(json, JsonOptions)
               ?? throw new InvalidOperationException("Failed to clone configuration.");
    }
}
