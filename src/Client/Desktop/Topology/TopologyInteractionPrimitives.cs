using Avalonia;

namespace Dna.Client.Desktop.Topology;

public sealed class TopologySpatialIndex
{
    private readonly List<(Rect Bounds, TopologyNodeViewModel Node)> _entries = [];

    public void Update(IEnumerable<(Rect Bounds, TopologyNodeViewModel Node)> entries)
    {
        _entries.Clear();
        _entries.AddRange(entries);
    }

    public TopologyNodeViewModel? HitTest(Point point)
    {
        foreach (var entry in _entries)
        {
            if (entry.Bounds.Contains(point))
                return entry.Node;
        }

        return null;
    }
}

public sealed class TopologyHitTester
{
    public TopologyNodeViewModel? HitTest(TopologySpatialIndex spatialIndex, Point graphPoint)
        => spatialIndex.HitTest(graphPoint);
}

public sealed class TopologyRenderListBuilder
{
    public TopologyRenderList Build(
        TopologyScene scene,
        IReadOnlyList<TopologyNodeViewModel> visibleNodes,
        IReadOnlyList<TopologyEdgeViewModel> visibleEdges,
        IReadOnlyDictionary<string, Point> layout,
        TopologyViewState viewState)
    {
        if (visibleNodes.Count == 0)
            return TopologyRenderList.Empty;

        var focusNodeId = viewState.SelectedNodeId ?? viewState.HoverNodeId;
        var connectedNodes = BuildConnectedNodes(visibleEdges, focusNodeId);

        var edgeItems = new List<TopologyEdgeRenderItem>(visibleEdges.Count);
        foreach (var edge in visibleEdges)
        {
            if (!layout.TryGetValue(edge.From, out var fromCenter) ||
                !layout.TryGetValue(edge.To, out var toCenter) ||
                scene.GetNodeOrDefault(edge.From) is not { } fromNode ||
                scene.GetNodeOrDefault(edge.To) is not { } toNode)
            {
                continue;
            }

            edgeItems.Add(new TopologyEdgeRenderItem(
                edge,
                fromNode,
                toNode,
                fromCenter,
                toCenter,
                TopologyGraphSemantics.ResolveRelationKey(edge),
                focusNodeId is null ||
                string.Equals(edge.From, focusNodeId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(edge.To, focusNodeId, StringComparison.OrdinalIgnoreCase)));
        }

        var nodeItems = visibleNodes
            .Where(node => layout.ContainsKey(node.Id))
            .Select(node => new TopologyNodeRenderItem(
                node,
                layout[node.Id],
                scene.GetChildCount(node.Id),
                string.Equals(viewState.SelectedNodeId, node.Id, StringComparison.OrdinalIgnoreCase),
                string.Equals(viewState.HoverNodeId, node.Id, StringComparison.OrdinalIgnoreCase),
                !string.IsNullOrWhiteSpace(viewState.ViewRootId) &&
                string.Equals(viewState.ViewRootId, node.Id, StringComparison.OrdinalIgnoreCase),
                focusNodeId is null ||
                connectedNodes.Contains(node.Id) ||
                string.Equals(focusNodeId, node.Id, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        return new TopologyRenderList(nodeItems, edgeItems, focusNodeId);
    }

    private static HashSet<string> BuildConnectedNodes(IReadOnlyList<TopologyEdgeViewModel> visibleEdges, string? focusNodeId)
    {
        var connected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(focusNodeId))
            return connected;

        connected.Add(focusNodeId);
        foreach (var edge in visibleEdges)
        {
            if (string.Equals(edge.From, focusNodeId, StringComparison.OrdinalIgnoreCase))
                connected.Add(edge.To);
            if (string.Equals(edge.To, focusNodeId, StringComparison.OrdinalIgnoreCase))
                connected.Add(edge.From);
        }

        return connected;
    }
}
