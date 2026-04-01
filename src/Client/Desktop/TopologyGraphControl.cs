using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Dna.Client.Desktop.Topology;

namespace Dna.Client.Desktop;

public sealed record TopologyNodeViewModel(
    string Id,
    string NodeId,
    string Label,
    string Type,
    string TypeLabel,
    string Discipline,
    string DisciplineLabel,
    int DependencyCount,
    string Summary,
    int? ComputedLayer,
    string? RelativePath = null,
    string? ParentModuleId = null,
    IReadOnlyList<string>? ChildModuleIds = null,
    IReadOnlyList<string>? ManagedPathScopes = null,
    string? Maintainer = null,
    string? Boundary = null,
    IReadOnlyList<string>? PublicApi = null,
    IReadOnlyList<string>? Constraints = null,
    IReadOnlyList<string>? Workflow = null,
    IReadOnlyList<string>? Rules = null,
    IReadOnlyList<string>? Prohibitions = null,
    IReadOnlyDictionary<string, string>? Metadata = null,
    bool CanEdit = false);

public sealed record TopologyEdgeViewModel(
    string From,
    string To,
    string Relation,
    bool IsComputed,
    string? Kind = null);

public sealed class TopologyGraphControl : Control
{
    private readonly Dictionary<string, Point> _layout = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<TopologyNodeViewModel> _visibleNodes = [];
    private readonly List<TopologyEdgeViewModel> _visibleEdges = [];
    private readonly TopologyViewState _viewState = new();
    private readonly TopologyLayoutOptions _layoutOptions = new();
    private readonly TopologyLayoutService _layoutService = new();
    private readonly TopologySpatialIndex _spatialIndex = new();
    private readonly TopologyHitTester _hitTester = new();
    private readonly TopologyRenderListBuilder _renderListBuilder = new();
    private readonly TopologyRenderListCache _renderListCache = new();
    private readonly AvaloniaTopologyRenderer _renderer = new();

    private TopologyScene _scene = TopologyScene.Empty;
    private bool _isPanning;
    private string? _draggingNodeId;
    private Point _dragStartGraphPoint;
    private Point _dragNodeOriginCenter;
    private Point _panOrigin;
    private bool _hasLayerData;
    private int _renderStructureRevision;
    private int _renderInteractionRevision;

    private string? _selectedNodeId
    {
        get => _viewState.SelectedNodeId;
        set => _viewState.SelectedNodeId = value;
    }

    private string? _hoverNodeId
    {
        get => _viewState.HoverNodeId;
        set => _viewState.HoverNodeId = value;
    }

    private string? _viewRootId
    {
        get => _viewState.ViewRootId;
        set => _viewState.ViewRootId = value;
    }

    private Vector _panOffset
    {
        get => _viewState.Viewport.PanOffset;
        set => _viewState.Viewport.PanOffset = value;
    }

    private double _zoom
    {
        get => _viewState.Viewport.Zoom;
        set => _viewState.Viewport.Zoom = value;
    }

    private bool _showDependency
    {
        get => _viewState.Filter.ShowDependency;
        set => _viewState.Filter.ShowDependency = value;
    }

    private bool _showComposition
    {
        get => _viewState.Filter.ShowComposition;
        set => _viewState.Filter.ShowComposition = value;
    }

    private bool _showAggregation
    {
        get => _viewState.Filter.ShowAggregation;
        set => _viewState.Filter.ShowAggregation = value;
    }

    private bool _showParentChild
    {
        get => _viewState.Filter.ShowParentChild;
        set => _viewState.Filter.ShowParentChild = value;
    }

    private bool _showCollaboration
    {
        get => _viewState.Filter.ShowCollaboration;
        set => _viewState.Filter.ShowCollaboration = value;
    }

    public event Action<TopologyNodeViewModel>? NodeSelected;
    public event Action<TopologyNodeViewModel>? NodeInvoked;
    public event Action? ScopeChanged;

    public string? ViewRootId => _viewRootId;

    public IReadOnlyList<string> VisibleNodeIds => _visibleNodes.Select(x => x.Id).ToArray();

    public IReadOnlyList<TopologyEdgeViewModel> VisibleEdges => _visibleEdges.ToArray();

    public IReadOnlyList<string> ScopeTrailIds => _scene.BuildScopeTrailIds(_viewRootId);

    public void SetTopology(IReadOnlyList<TopologyNodeViewModel> nodes, IReadOnlyList<TopologyEdgeViewModel> edges)
    {
        _scene = TopologyScene.Create(nodes, edges);
        _hasLayerData = _scene.HasLayerData;

        if (_selectedNodeId is not null && !_scene.ContainsNode(_selectedNodeId))
            SetSelectedNodeId(null, invalidateVisual: false);

        if (_viewRootId is not null && !_scene.ContainsNode(_viewRootId))
            _viewRootId = null;

        RebuildVisibleGraph(resetViewport: true);
        ScopeChanged?.Invoke();
    }

    public void ClearTopology()
    {
        _layout.Clear();
        _visibleNodes.Clear();
        _visibleEdges.Clear();
        _scene = TopologyScene.Empty;
        _viewState.ClearInteractiveState();
        _hasLayerData = false;
        _renderListCache.Invalidate();
        MarkRenderStructureDirty(invalidateVisual: false);
        MarkInteractionDirty(invalidateVisual: false);
        ResetViewport();
        _spatialIndex.Update([]);
        InvalidateVisual();
        ScopeChanged?.Invoke();
    }

    public void SelectNode(string? nodeId)
    {
        if (string.IsNullOrWhiteSpace(nodeId) ||
            _visibleNodes.All(x => !string.Equals(x.Id, nodeId, StringComparison.OrdinalIgnoreCase)))
        {
            SetSelectedNodeId(null);
            return;
        }

        SetSelectedNodeId(nodeId);
    }

    public bool CanNavigateInto(string? nodeId) => _scene.CanNavigateInto(nodeId);

    public int GetChildCount(string? nodeId) => _scene.GetChildCount(nodeId);

    public bool NavigateInto(string? nodeId)
    {
        if (!CanNavigateInto(nodeId))
            return false;

        _viewRootId = nodeId;
        RebuildVisibleGraph(resetViewport: true);
        ScopeChanged?.Invoke();
        return true;
    }

    public bool NavigateUp()
    {
        if (string.IsNullOrWhiteSpace(_viewRootId))
            return false;

        _viewRootId = _scene.ResolveParent(_viewRootId);
        RebuildVisibleGraph(resetViewport: true);
        ScopeChanged?.Invoke();
        return true;
    }

    public void NavigateRoot()
    {
        _viewRootId = null;
        RebuildVisibleGraph(resetViewport: true);
        ScopeChanged?.Invoke();
    }

    public void SetRelationFilter(bool dependency, bool composition, bool aggregation, bool parentChild, bool collaboration)
    {
        _showDependency = dependency;
        _showComposition = composition;
        _showAggregation = aggregation;
        _showParentChild = parentChild;
        _showCollaboration = collaboration;
        RebuildVisibleGraph(resetViewport: false);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        var graphPoint = ScreenToGraph(e.GetPosition(this));
        var hit = _hitTester.HitTest(_spatialIndex, graphPoint);
        if (hit is not null)
        {
            SetSelectedNodeId(hit.Id, invalidateVisual: false);
            NodeSelected?.Invoke(hit);

            if (e.ClickCount >= 2)
            {
                NodeInvoked?.Invoke(hit);
                InvalidateVisual();
                e.Handled = true;
                return;
            }

            if (IsPinnedScopeNode(hit.Id))
            {
                InvalidateVisual();
                e.Handled = true;
                return;
            }

            if (_layout.TryGetValue(hit.Id, out var center))
            {
                _draggingNodeId = hit.Id;
                _dragStartGraphPoint = graphPoint;
                _dragNodeOriginCenter = center;
                e.Pointer.Capture(this);
            }

            InvalidateVisual();
            e.Handled = true;
            return;
        }

        _isPanning = true;
        _panOrigin = e.GetPosition(this);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_draggingNodeId is not null && _layout.ContainsKey(_draggingNodeId))
        {
            var nowGraph = ScreenToGraph(e.GetPosition(this));
            var delta = nowGraph - _dragStartGraphPoint;
            _layout[_draggingNodeId] = new Point(_dragNodeOriginCenter.X + delta.X, _dragNodeOriginCenter.Y + delta.Y);
            RefreshSpatialIndex();
            MarkRenderStructureDirty();
            e.Handled = true;
            return;
        }

        if (!_isPanning)
        {
            var hoverNode = _hitTester.HitTest(_spatialIndex, ScreenToGraph(e.GetPosition(this)));
            var nextHoverId = hoverNode?.Id;
            SetHoverNodeId(nextHoverId);

            return;
        }

        var now = e.GetPosition(this);
        var panDelta = now - _panOrigin;
        _panOrigin = now;
        _panOffset += panDelta;
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_draggingNodeId is not null)
        {
            _draggingNodeId = null;
            e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }

        if (_isPanning)
        {
            _isPanning = false;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        SetHoverNodeId(null);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        var cursor = e.GetPosition(this);
        var before = ScreenToGraph(cursor);
        var factor = e.Delta.Y > 0 ? 1.12 : 0.9;
        _zoom = Math.Clamp(_zoom * factor, 0.42, 2.4);

        var after = GraphToScreen(before);
        _panOffset += cursor - after;

        InvalidateVisual();
        e.Handled = true;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var renderList = _renderListCache.GetOrCreate(
            new TopologyRenderListCacheKey(_renderStructureRevision, _renderInteractionRevision),
            () => _renderListBuilder.Build(_scene, _visibleNodes, _visibleEdges, _layout, _viewState));
        _renderer.Render(context, Bounds, renderList, _viewState.Viewport, _layoutOptions, _viewRootId, _hasLayerData);
    }

    private void RebuildVisibleGraph(bool resetViewport)
    {
        _visibleNodes.Clear();
        _visibleEdges.Clear();
        _layout.Clear();

        var visibleGraph = _scene.ResolveVisibleGraph(_viewState.Filter, _viewRootId);
        _visibleNodes.AddRange(visibleGraph.Nodes);
        _visibleEdges.AddRange(visibleGraph.Edges);

        if (_selectedNodeId is not null && !visibleGraph.VisibleNodeIds.Contains(_selectedNodeId))
            SetSelectedNodeId(null, invalidateVisual: false);

        if (_hoverNodeId is not null && !visibleGraph.VisibleNodeIds.Contains(_hoverNodeId))
            SetHoverNodeId(null, invalidateVisual: false);

        RebuildLayout();
        if (resetViewport)
            ResetViewport();

        RefreshSpatialIndex();
        MarkRenderStructureDirty();
    }

    private void RebuildLayout()
    {
        if (_visibleNodes.Count == 0)
            return;

        var visibleGraph = new TopologyVisibleGraph(
            _visibleNodes.ToList(),
            _visibleEdges.ToList(),
            new HashSet<string>(_visibleNodes.Select(node => node.Id), StringComparer.OrdinalIgnoreCase));
        var layout = _layoutService.Layout(visibleGraph, _scene, _layoutOptions, _viewRootId);
        foreach (var pair in layout.NodeCenters)
            _layout[pair.Key] = pair.Value;
    }

    private bool SetSelectedNodeId(string? nodeId, bool invalidateVisual = true)
    {
        if (string.Equals(_selectedNodeId, nodeId, StringComparison.OrdinalIgnoreCase))
            return false;

        _selectedNodeId = nodeId;
        MarkInteractionDirty(invalidateVisual);
        return true;
    }

    private bool SetHoverNodeId(string? nodeId, bool invalidateVisual = true)
    {
        if (string.Equals(_hoverNodeId, nodeId, StringComparison.OrdinalIgnoreCase))
            return false;

        _hoverNodeId = nodeId;
        MarkInteractionDirty(invalidateVisual);
        return true;
    }

    private bool IsPinnedScopeNode(string nodeId)
    {
        return !string.IsNullOrWhiteSpace(_viewRootId) &&
               string.Equals(nodeId, _viewRootId, StringComparison.OrdinalIgnoreCase) &&
               _visibleNodes.Count > 1;
    }

    private Point GraphToScreen(Point graphPoint)
        => _viewState.Viewport.GraphToScreen(Bounds.Size, graphPoint);

    private Point ScreenToGraph(Point screenPoint)
        => _viewState.Viewport.ScreenToGraph(Bounds.Size, screenPoint);

    private Rect BuildNodeRectInGraph(Point graphCenter, TopologyNodeViewModel node)
    {
        var (width, height) = GetNodeSize(node);
        return new Rect(graphCenter.X - width / 2, graphCenter.Y - height / 2, width, height);
    }

    private bool IsScopeNode(string nodeId)
    {
        return !string.IsNullOrWhiteSpace(_viewRootId) &&
               string.Equals(nodeId, _viewRootId, StringComparison.OrdinalIgnoreCase);
    }

    private (double Width, double Height) GetNodeSize(TopologyNodeViewModel node)
    {
        return IsScopeNode(node.Id)
            ? (_layoutOptions.ScopeNodeWidth, _layoutOptions.ScopeNodeHeight)
            : (_layoutOptions.NodeWidth, _layoutOptions.NodeHeight);
    }

    private void ResetViewport()
    {
        _viewState.Viewport.Reset();
    }

    private void MarkRenderStructureDirty(bool invalidateVisual = true)
    {
        _renderStructureRevision++;
        _renderListCache.Invalidate();
        if (invalidateVisual)
            InvalidateVisual();
    }

    private void MarkInteractionDirty(bool invalidateVisual = true)
    {
        _renderInteractionRevision++;
        _renderListCache.Invalidate();
        if (invalidateVisual)
            InvalidateVisual();
    }

    private void RefreshSpatialIndex()
    {
        var entries = _visibleNodes
            .Where(node => _layout.ContainsKey(node.Id))
            .Select(node => (BuildNodeRectInGraph(_layout[node.Id], node), node));
        _spatialIndex.Update(entries);
    }
}
