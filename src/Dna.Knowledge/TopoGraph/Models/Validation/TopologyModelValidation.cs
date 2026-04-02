using Dna.Knowledge.TopoGraph.Models.Snapshots;

namespace Dna.Knowledge.TopoGraph.Models.Validation;

public enum TopologyValidationSeverity
{
    Error,
    Warning
}

public sealed class TopologyValidationIssue
{
    public TopologyValidationSeverity Severity { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? NodeId { get; init; }
}

public sealed class TopologyModelBuildResult
{
    public TopologyModelSnapshot Snapshot { get; init; } = new();
    public List<TopologyValidationIssue> Issues { get; init; } = [];

    public bool HasErrors => Issues.Any(item => item.Severity == TopologyValidationSeverity.Error);
}
