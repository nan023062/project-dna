namespace Dna.Knowledge.TopoGraph.Models.ValueObjects;

public sealed class TopologyKnowledgeSummary
{
    public string? Identity { get; init; }
    public List<TopologyLessonSummary> Lessons { get; init; } = [];
    public List<string> Facts { get; init; } = [];
    public List<string> MemoryIds { get; init; } = [];
}

public sealed class TopologyLessonSummary
{
    public string Title { get; init; } = string.Empty;
    public string? Severity { get; init; }
    public string? Resolution { get; init; }
}

public sealed class ModuleContract
{
    public string? Boundary { get; init; }
    public List<string> PublicApi { get; init; } = [];
    public List<string> Constraints { get; init; } = [];
}

public sealed class ModulePathBinding
{
    public string? MainPath { get; init; }
    public List<string> ManagedPaths { get; init; } = [];

    public IReadOnlyList<string> GetAllPaths()
    {
        var values = new List<string>();

        AddPath(values, MainPath);
        foreach (var path in ManagedPaths)
            AddPath(values, path);

        return values;
    }

    private static void AddPath(List<string> values, string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
            return;

        var normalized = rawPath.Replace('\\', '/').Trim().Trim('/');
        if (normalized.Length == 0)
            return;

        if (!values.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            values.Add(normalized);
    }
}
