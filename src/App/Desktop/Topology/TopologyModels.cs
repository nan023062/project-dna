using Avalonia;

namespace Dna.App.Desktop.Topology;

public sealed class TopologyFilterState
{
    public bool ShowDependency { get; set; } = true;
    public bool ShowComposition { get; set; } = true;
    public bool ShowAggregation { get; set; } = true;
    public bool ShowParentChild { get; set; } = true;
    public bool ShowCollaboration { get; set; } = true;
}

public sealed class TopologyViewportState
{
    public Vector PanOffset { get; set; } = new(0, 0);
    public double Zoom { get; set; } = 1.0;

    public void Reset()
    {
        PanOffset = new Vector(0, 0);
        Zoom = 1.0;
    }

    public Point GraphToScreen(Size bounds, Point graphPoint)
    {
        var center = new Point(bounds.Width / 2, bounds.Height / 2);
        return new Point(center.X + PanOffset.X + graphPoint.X * Zoom, center.Y + PanOffset.Y + graphPoint.Y * Zoom);
    }

    public Point ScreenToGraph(Size bounds, Point screenPoint)
    {
        var center = new Point(bounds.Width / 2, bounds.Height / 2);
        return new Point((screenPoint.X - center.X - PanOffset.X) / Zoom, (screenPoint.Y - center.Y - PanOffset.Y) / Zoom);
    }
}

public sealed class TopologyViewState
{
    public string? SelectedNodeId { get; set; }
    public string? HoverNodeId { get; set; }
    public string? ViewRootId { get; set; }
    public TopologyFilterState Filter { get; } = new();
    public TopologyViewportState Viewport { get; } = new();

    public void ClearInteractiveState()
    {
        SelectedNodeId = null;
        HoverNodeId = null;
        ViewRootId = null;
        Viewport.Reset();
    }
}

public sealed class TopologyVisibleGraph
{
    public static TopologyVisibleGraph Empty { get; } = new([], [], new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    public TopologyVisibleGraph(
        IReadOnlyList<TopologyNodeViewModel> nodes,
        IReadOnlyList<TopologyEdgeViewModel> edges,
        HashSet<string> visibleNodeIds)
    {
        Nodes = nodes;
        Edges = edges;
        VisibleNodeIds = visibleNodeIds;
    }

    public IReadOnlyList<TopologyNodeViewModel> Nodes { get; }
    public IReadOnlyList<TopologyEdgeViewModel> Edges { get; }
    public HashSet<string> VisibleNodeIds { get; }
}

public sealed class TopologyLayoutOptions
{
    public double NodeWidth { get; init; } = 220;
    public double NodeHeight { get; init; } = 88;
    public double ScopeNodeWidth { get; init; } = 248;
    public double ScopeNodeHeight { get; init; } = 100;
    public double ColumnGap { get; init; } = 284;
    public double RowGap { get; init; } = 176;
    public int ScopedRowCapacity { get; init; } = 4;
    public double ScopedParentCenterY { get; init; } = -188;
    public double ScopedOrbitStartY { get; init; } = 88;
}

public sealed class TopologyLayoutResult
{
    public static TopologyLayoutResult Empty { get; } = new(new Dictionary<string, Point>(StringComparer.OrdinalIgnoreCase));

    public TopologyLayoutResult(Dictionary<string, Point> nodeCenters)
    {
        NodeCenters = nodeCenters;
    }

    public Dictionary<string, Point> NodeCenters { get; }
}

public sealed record TopologyNodeRenderItem(
    TopologyNodeViewModel Node,
    Point GraphPoint,
    int ChildCount,
    bool IsSelected,
    bool IsHovered,
    bool IsScopeCenter,
    bool IsConnected);

public sealed record TopologyEdgeRenderItem(
    TopologyEdgeViewModel Edge,
    TopologyNodeViewModel FromNode,
    TopologyNodeViewModel ToNode,
    Point FromCenter,
    Point ToCenter,
    string RelationKey,
    bool IsConnected);

public sealed class TopologyRenderList
{
    public static TopologyRenderList Empty { get; } = new([], [], null);

    public TopologyRenderList(
        IReadOnlyList<TopologyNodeRenderItem> nodes,
        IReadOnlyList<TopologyEdgeRenderItem> edges,
        string? focusNodeId)
    {
        Nodes = nodes;
        Edges = edges;
        FocusNodeId = focusNodeId;
    }

    public IReadOnlyList<TopologyNodeRenderItem> Nodes { get; }
    public IReadOnlyList<TopologyEdgeRenderItem> Edges { get; }
    public string? FocusNodeId { get; }
}
