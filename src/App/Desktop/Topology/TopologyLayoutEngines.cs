using Avalonia;

namespace Dna.App.Desktop.Topology;

public interface ITopologyLayoutEngine
{
    bool CanHandle(TopologyVisibleGraph graph, TopologyScene scene, string? viewRootId);

    TopologyLayoutResult Layout(
        TopologyVisibleGraph graph,
        TopologyScene scene,
        TopologyLayoutOptions options,
        string? viewRootId);
}

public sealed class ScopedTopologyLayoutEngine : ITopologyLayoutEngine
{
    public bool CanHandle(TopologyVisibleGraph graph, TopologyScene scene, string? viewRootId)
    {
        return !string.IsNullOrWhiteSpace(viewRootId) &&
               scene.ContainsNode(viewRootId) &&
               graph.Nodes.Any(node => string.Equals(node.Id, viewRootId, StringComparison.OrdinalIgnoreCase));
    }

    public TopologyLayoutResult Layout(
        TopologyVisibleGraph graph,
        TopologyScene scene,
        TopologyLayoutOptions options,
        string? viewRootId)
    {
        var result = new Dictionary<string, Point>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(viewRootId))
            return new TopologyLayoutResult(result);

        result[viewRootId] = new Point(0, options.ScopedParentCenterY);

        var orbitNodes = graph.Nodes
            .Where(node => !string.Equals(node.Id, viewRootId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(node => string.IsNullOrWhiteSpace(node.DisciplineLabel) ? "unknown" : node.DisciplineLabel, StringComparer.OrdinalIgnoreCase)
            .ThenBy(node => TopologyGraphSemantics.ResolveTypeOrder(node.Type))
            .ThenBy(node => node.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var consumed = 0;
        var rowIndex = 0;
        while (consumed < orbitNodes.Count)
        {
            var rowCount = Math.Min(orbitNodes.Count - consumed, options.ScopedRowCapacity);
            var totalWidth = Math.Max(0, (rowCount - 1) * options.ColumnGap);
            var startX = -totalWidth / 2.0;
            var rowY = options.ScopedOrbitStartY + rowIndex * options.RowGap;

            for (var i = 0; i < rowCount; i++)
            {
                var node = orbitNodes[consumed + i];
                result[node.Id] = new Point(startX + i * options.ColumnGap, rowY);
            }

            consumed += rowCount;
            rowIndex++;
        }

        return new TopologyLayoutResult(result);
    }
}

public sealed class LayeredTopologyLayoutEngine : ITopologyLayoutEngine
{
    public bool CanHandle(TopologyVisibleGraph graph, TopologyScene scene, string? viewRootId)
    {
        _ = scene;
        _ = viewRootId;
        return graph.Nodes.Count > 0;
    }

    public TopologyLayoutResult Layout(
        TopologyVisibleGraph graph,
        TopologyScene scene,
        TopologyLayoutOptions options,
        string? viewRootId)
    {
        _ = scene;
        _ = viewRootId;

        var result = new Dictionary<string, Point>(StringComparer.OrdinalIgnoreCase);
        var layerGroups = graph.Nodes.GroupBy(TopologyGraphSemantics.ResolveLayer).OrderBy(group => group.Key).ToList();
        var totalHeight = Math.Max(0, (layerGroups.Count - 1) * options.RowGap);
        var startY = -totalHeight / 2.0;

        for (var layerIndex = 0; layerIndex < layerGroups.Count; layerIndex++)
        {
            var sorted = layerGroups[layerIndex]
                .OrderBy(node => string.IsNullOrWhiteSpace(node.DisciplineLabel) ? "unknown" : node.DisciplineLabel, StringComparer.OrdinalIgnoreCase)
                .ThenBy(node => TopologyGraphSemantics.ResolveTypeOrder(node.Type))
                .ThenBy(node => node.Label, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var totalWidth = Math.Max(0, (sorted.Count - 1) * options.ColumnGap);
            var startX = -totalWidth / 2.0;
            var rowY = startY + layerIndex * options.RowGap;

            for (var index = 0; index < sorted.Count; index++)
                result[sorted[index].Id] = new Point(startX + index * options.ColumnGap, rowY);
        }

        return new TopologyLayoutResult(result);
    }
}

public sealed class TopologyLayoutService
{
    private readonly IReadOnlyList<ITopologyLayoutEngine> _engines;

    public TopologyLayoutService(params ITopologyLayoutEngine[] engines)
    {
        _engines = engines.Length == 0
            ? [new ScopedTopologyLayoutEngine(), new LayeredTopologyLayoutEngine()]
            : engines;
    }

    public TopologyLayoutResult Layout(
        TopologyVisibleGraph graph,
        TopologyScene scene,
        TopologyLayoutOptions options,
        string? viewRootId)
    {
        foreach (var engine in _engines)
        {
            if (engine.CanHandle(graph, scene, viewRootId))
                return engine.Layout(graph, scene, options, viewRootId);
        }

        return TopologyLayoutResult.Empty;
    }
}
