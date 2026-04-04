using System.Text.Json.Serialization;

namespace Dna.Workbench.Agent.Pipeline;

public sealed class AgentExecutionPipelineConfig
{
    public int Version { get; set; } = 1;
    public bool Enabled { get; set; } = true;
    public bool RequireArchitectBeforeDeveloper { get; set; } = true;
    public bool StrictGate { get; set; } = true;
    public bool PersistRunAsMemory { get; set; } = true;
    public List<string> ExecutionOrder { get; set; } =
    [
        AgentPipelineConstants.SlotIds.Architect,
        AgentPipelineConstants.SlotIds.Developer
    ];

    public Dictionary<string, AgentSlotConfig> Slots { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        [AgentPipelineConstants.SlotIds.Architect] = AgentSlotConfig.CreateArchitectDefault(),
        [AgentPipelineConstants.SlotIds.Developer] = AgentSlotConfig.CreateDeveloperDefault()
    };
}

public sealed class AgentSlotConfig
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public bool BlockingOnFailure { get; set; } = true;
    public List<string> DefaultModules { get; set; } = [];
    public List<string> RecallQuestions { get; set; } = [];
    public List<string> Objectives { get; set; } = [];

    public static AgentSlotConfig CreateArchitectDefault() => new()
    {
        Id = AgentPipelineConstants.SlotIds.Architect,
        DisplayName = AgentPipelineConstants.SlotDisplayNames.Architect,
        Enabled = true,
        BlockingOnFailure = true,
        DefaultModules = ["Dna.App"],
        RecallQuestions =
        [
            "当前任务涉及哪些架构约束、教训和风险？",
            "是否有历史决策会影响本次改动？"
        ],
        Objectives =
        [
            "先复盘，再开发。",
            "输出架构风险与修复建议。",
            "确保单向依赖和可扩展性。"
        ]
    };

    public static AgentSlotConfig CreateDeveloperDefault() => new()
    {
        Id = AgentPipelineConstants.SlotIds.Developer,
        DisplayName = AgentPipelineConstants.SlotDisplayNames.Developer,
        Enabled = true,
        BlockingOnFailure = false,
        DefaultModules = ["Dna.App"],
        RecallQuestions =
        [
            "本次开发必须遵守的实现约束是什么？",
            "有哪些已知教训可以避免重复犯错？"
        ],
        Objectives =
        [
            "基于复盘结论执行开发。",
            "保持实现与架构约束一致。",
            "完成后回写记忆。"
        ]
    };
}

public sealed class PipelineRunRequest
{
    public string? Task { get; set; }
    public List<string>? Modules { get; set; }
    public bool DryRun { get; set; }
    public bool? StrictGate { get; set; }
    public bool? PersistRunAsMemory { get; set; }
}

public sealed class PipelineRunResult
{
    public string RunId { get; init; } = Guid.NewGuid().ToString("N");
    public string Status { get; set; } = "Success";
    public string? BlockedReason { get; set; }
    public DateTime StartedAt { get; init; } = DateTime.UtcNow;
    public DateTime FinishedAt { get; set; } = DateTime.UtcNow;
    public string? Task { get; set; }
    public List<string> Modules { get; set; } = [];
    public bool DryRun { get; set; }
    public bool StrictGate { get; set; } = true;
    public List<SlotRunResult> Slots { get; set; } = [];
}

public sealed class SlotRunResult
{
    public string SlotId { get; init; } = string.Empty;
    public string SlotName { get; init; } = string.Empty;
    public string Status { get; set; } = "Success";
    public DateTime StartedAt { get; init; } = DateTime.UtcNow;
    public DateTime FinishedAt { get; set; } = DateTime.UtcNow;
    public string? Message { get; set; }
    public List<string> Findings { get; set; } = [];
    public Dictionary<string, object?> Outputs { get; set; } = new();
}

public sealed class SlotRunState
{
    public bool ArchitectPassed { get; set; }
}

public sealed class PipelineUpdateRequest
{
    [JsonPropertyName("config")]
    public AgentExecutionPipelineConfig? Config { get; set; }
}
