using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

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
    private const double NodeWidth = 220;
    private const double NodeHeight = 88;
    private const double ScopeNodeWidth = 248;
    private const double ScopeNodeHeight = 100;
    private const double ColumnGap = 284;
    private const double RowGap = 176;
    private const int ScopedRowCapacity = 4;

    private static readonly Color BackgroundColor = Color.Parse("#F8FAFC");
    private static readonly Color GridColor = Color.Parse("#CBD5E1");
    private static readonly Color BorderColor = Color.Parse("#D0D5DD");
    private static readonly Color LabelColor = Color.Parse("#101828");
    private static readonly Color MetaColor = Color.Parse("#667085");
    private static readonly Color HintColor = Color.Parse("#475467");
    private static readonly Color SurfaceStrokeColor = Color.Parse("#FFFFFF");

    private readonly Dictionary<string, TopologyNodeViewModel> _nodeMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Point> _layout = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _parentByChild = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<string>> _childrenByParent = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _rootNodeIds = [];
    private readonly List<TopologyNodeViewModel> _visibleNodes = [];
    private readonly List<TopologyEdgeViewModel> _visibleEdges = [];

    private IReadOnlyList<TopologyNodeViewModel> _nodes = [];
    private IReadOnlyList<TopologyEdgeViewModel> _edges = [];
    private string? _selectedNodeId;
    private string? _hoverNodeId;
    private string? _viewRootId;
    private bool _isPanning;
    private string? _draggingNodeId;
    private Point _dragStartGraphPoint;
    private Point _dragNodeOriginCenter;
    private Point _panOrigin;
    private Vector _panOffset;
    private double _zoom = 1.0;
    private bool _showDependency = true;
    private bool _showComposition = true;
    private bool _showAggregation = true;
    private bool _showParentChild = true;
    private bool _showCollaboration = true;
    private bool _hasLayerData;

    public event Action<TopologyNodeViewModel>? NodeSelected;
    public event Action<TopologyNodeViewModel>? NodeInvoked;
    public event Action? ScopeChanged;

    public string? ViewRootId => _viewRootId;

    public IReadOnlyList<string> VisibleNodeIds => _visibleNodes.Select(x => x.Id).ToArray();

    public IReadOnlyList<TopologyEdgeViewModel> VisibleEdges => _visibleEdges.ToArray();

    public IReadOnlyList<string> ScopeTrailIds => BuildScopeTrailIds();

    public void SetTopology(IReadOnlyList<TopologyNodeViewModel> nodes, IReadOnlyList<TopologyEdgeViewModel> edges)
    {
        _nodes = nodes;
        _edges = edges;
        _hasLayerData = _nodes.Any(n => n.ComputedLayer.HasValue);

        _nodeMap.Clear();
        foreach (var node in nodes)
            _nodeMap[node.Id] = node;

        BuildHierarchy();

        if (_selectedNodeId is not null && !_nodeMap.ContainsKey(_selectedNodeId))
            _selectedNodeId = null;

        if (_viewRootId is not null && !_nodeMap.ContainsKey(_viewRootId))
            _viewRootId = null;

        RebuildVisibleGraph(resetViewport: true);
        ScopeChanged?.Invoke();
    }

    public void ClearTopology()
    {
        _nodes = [];
        _edges = [];
        _nodeMap.Clear();
        _parentByChild.Clear();
        _childrenByParent.Clear();
        _rootNodeIds.Clear();
        _layout.Clear();
        _visibleNodes.Clear();
        _visibleEdges.Clear();
        _selectedNodeId = null;
        _hoverNodeId = null;
        _viewRootId = null;
        _hasLayerData = false;
        ResetViewport();
        InvalidateVisual();
        ScopeChanged?.Invoke();
    }

    public void SelectNode(string? nodeId)
    {
        if (string.IsNullOrWhiteSpace(nodeId) || _visibleNodes.All(x => !string.Equals(x.Id, nodeId, StringComparison.OrdinalIgnoreCase)))
        {
            _selectedNodeId = null;
            InvalidateVisual();
            return;
        }

        _selectedNodeId = nodeId;
        InvalidateVisual();
    }

    public bool CanNavigateInto(string? nodeId)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
            return false;

        return ResolveScopedChildIds(nodeId).Count > 0;
    }

    public int GetChildCount(string? nodeId)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
            return 0;

        return ResolveScopedChildIds(nodeId).Count;
    }

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

        _viewRootId = _parentByChild.TryGetValue(_viewRootId, out var parentId) ? parentId : null;
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
        var hit = HitTestNode(graphPoint);
        if (hit is not null)
        {
            _selectedNodeId = hit.Id;
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
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (!_isPanning)
        {
            var hoverNode = HitTestNode(ScreenToGraph(e.GetPosition(this)));
            var nextHoverId = hoverNode?.Id;
            if (!string.Equals(nextHoverId, _hoverNodeId, StringComparison.OrdinalIgnoreCase))
            {
                _hoverNodeId = nextHoverId;
                InvalidateVisual();
            }

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
        if (_hoverNodeId is null)
            return;

        _hoverNodeId = null;
        InvalidateVisual();
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
        context.FillRectangle(new SolidColorBrush(BackgroundColor), Bounds);
        DrawGrid(context);

        if (_visibleNodes.Count == 0)
        {
            DrawCenteredMessage(context, "当前层级暂无可显示节点");
            return;
        }

        DrawEdges(context);
        DrawNodes(context);
    }

    private void DrawGrid(DrawingContext context)
    {
        var brush = new SolidColorBrush(Color.FromArgb(34, GridColor.R, GridColor.G, GridColor.B));
        const double step = 44;
        const double radius = 1.1;

        for (var y = step / 2.0; y < Bounds.Height; y += step)
        {
            for (var x = step / 2.0; x < Bounds.Width; x += step)
                context.DrawEllipse(brush, null, new Point(x, y), radius, radius);
        }
    }

    private void DrawEdges(DrawingContext context)
    {
        var focusNodeId = _selectedNodeId ?? _hoverNodeId;
        foreach (var edge in _visibleEdges)
        {
            var relationKey = ResolveRelationKey(edge);
            if (!_layout.TryGetValue(edge.From, out var fromCenter) ||
                !_layout.TryGetValue(edge.To, out var toCenter) ||
                !_nodeMap.TryGetValue(edge.From, out var fromNode) ||
                !_nodeMap.TryGetValue(edge.To, out var toNode))
            {
                continue;
            }

            var fromRect = BuildNodeRectInScreen(fromCenter, fromNode);
            var toRect = BuildNodeRectInScreen(toCenter, toNode);
            var (start, end, horizontal) = GetAnchors(fromRect, toRect);
            var route = BuildOrthogonalRoute(start, end, horizontal);

            var color = EdgeColor(relationKey);
            var thickness = edge.IsComputed ? 0.95 : 1.35;
            DashStyle? dashStyle = null;
            if (string.Equals(relationKey, "collaboration", StringComparison.OrdinalIgnoreCase))
                dashStyle = new DashStyle([6, 4], 0);
            else if (string.Equals(relationKey, "aggregation", StringComparison.OrdinalIgnoreCase))
                dashStyle = new DashStyle([4, 4], 0);
            else if (string.Equals(relationKey, "parentchild", StringComparison.OrdinalIgnoreCase))
                dashStyle = new DashStyle([3, 6], 0);

            if (focusNodeId is not null)
            {
                var connected = string.Equals(edge.From, focusNodeId, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(edge.To, focusNodeId, StringComparison.OrdinalIgnoreCase);
                if (!connected)
                    color = Color.FromArgb(80, color.R, color.G, color.B);
                else
                    thickness += 0.45;
            }

            var geometry = BuildRoundedOrthogonalGeometry(route, 14);

            context.DrawGeometry(null, new Pen(new SolidColorBrush(color), thickness) { DashStyle = dashStyle }, geometry);

            if (string.Equals(relationKey, "composition", StringComparison.OrdinalIgnoreCase))
                DrawDiamond(context, route[0], route[1], color, filled: true);
            else if (string.Equals(relationKey, "aggregation", StringComparison.OrdinalIgnoreCase))
                DrawDiamond(context, route[0], route[1], color, filled: false);

            DrawArrow(context, route[^1], route[^2], color);
        }
    }

    private void DrawNodes(DrawingContext context)
    {
        var focusNodeId = _selectedNodeId ?? _hoverNodeId;
        var connectedNodes = focusNodeId is null ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) : BuildConnectedNodes(focusNodeId);

        foreach (var node in _visibleNodes)
        {
            if (!_layout.TryGetValue(node.Id, out var graphPoint))
                continue;

            var rect = BuildNodeRectInScreen(graphPoint, node);
            var isSelected = string.Equals(_selectedNodeId, node.Id, StringComparison.OrdinalIgnoreCase);
            var isFocusNode = focusNodeId is not null && string.Equals(focusNodeId, node.Id, StringComparison.OrdinalIgnoreCase);
            var isHovered = string.Equals(_hoverNodeId, node.Id, StringComparison.OrdinalIgnoreCase);
            var isScopeCenter = IsScopeNode(node.Id);
            var isConnected = focusNodeId is null || isFocusNode || connectedNodes.Contains(node.Id);
            var alpha = isConnected ? (byte)255 : (byte)85;
            var childCount = GetChildCount(node.Id);
            var accent = WithAlpha(NodeAccent(node), alpha);

            var fill = WithAlpha(NodeFill(node, isScopeCenter), alpha);
            var border = isSelected
                ? Color.Parse("#2F6FED")
                : isScopeCenter
                    ? Color.Parse("#2E90FA")
                    : BorderColor;
            border = WithAlpha(border, alpha);

            if (isSelected || isHovered)
            {
                var halo = ExpandRect(rect, 4 * _zoom, 4 * _zoom);
                var haloColor = isSelected
                    ? Color.FromArgb(Math.Min(alpha, (byte)28), 47, 111, 237)
                    : Color.FromArgb(Math.Min(alpha, (byte)20), 152, 162, 179);
                context.DrawRectangle(new SolidColorBrush(haloColor), null, halo, 16 * _zoom);
            }

            context.DrawRectangle(
                new SolidColorBrush(fill),
                new Pen(new SolidColorBrush(border), isSelected ? 2.1 : isScopeCenter ? 1.6 : 1.0),
                rect,
                14 * _zoom);

            DrawNodeTexts(context, node, rect, alpha, childCount, isScopeCenter);
        }
    }

    private void DrawNodeTexts(DrawingContext context, TopologyNodeViewModel node, Rect rect, byte alpha, int childCount, bool isScopeCenter)
    {
        var paddingX = Math.Clamp(14 * _zoom, 12, 20);
        var paddingY = Math.Clamp(12 * _zoom, 10, 18);
        var badgeFontSize = Math.Clamp(8.6 * _zoom, 7, 11);
        var titleSize = Math.Clamp((isScopeCenter ? 14.2 : 13.0) * _zoom, 10, 18);
        var metaSize = Math.Clamp(9.0 * _zoom, 8, 12);
        var actionFontSize = Math.Clamp(8.2 * _zoom, 7, 11);
        var badgeText = ResolveNodeBadge(node);
        var accent = WithAlpha(NodeAccent(node), alpha);

        var badge = new FormattedText(
            badgeText,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            badgeFontSize,
            new SolidColorBrush(WithAlpha(LabelColor, alpha)));
        var badgeHeight = Math.Max(16, badge.Height + 4);
        var badgeWidth = Math.Max(36, badge.Width + 10);
        var badgeRect = new Rect(rect.X + paddingX, rect.Y + paddingY, badgeWidth, badgeHeight);
        context.DrawRectangle(
            new SolidColorBrush(Color.FromArgb(Math.Min(alpha, (byte)16), SurfaceStrokeColor.R, SurfaceStrokeColor.G, SurfaceStrokeColor.B)),
            new Pen(new SolidColorBrush(Color.FromArgb(Math.Min(alpha, (byte)46), BorderColor.R, BorderColor.G, BorderColor.B)), 1),
            badgeRect,
            8 * _zoom);
        context.DrawText(
            badge,
            new Point(
                badgeRect.X + (badgeRect.Width - badge.Width) / 2,
                badgeRect.Y + (badgeRect.Height - badge.Height) / 2 - 0.5));

        if (isScopeCenter)
        {
            DrawActionPill(
                context,
                "当前作用域",
                rect.Right - paddingX,
                rect.Y + paddingY,
                actionFontSize,
                alpha,
                Color.Parse("#2F6FED"),
                filled: false,
                alignRight: true);
        }

        var titleTop = badgeRect.Bottom + 9 * _zoom;
        var title = new FormattedText(
            node.Label,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            titleSize,
            new SolidColorBrush(WithAlpha(LabelColor, alpha)))
        {
            TextAlignment = TextAlignment.Left,
            MaxTextWidth = Math.Max(48, rect.Width - paddingX * 2),
            MaxTextHeight = Math.Max(28, rect.Height * 0.34),
            Trimming = TextTrimming.CharacterEllipsis
        };

        context.DrawText(title, new Point(rect.X + paddingX, titleTop));

        var meta = new FormattedText(
            BuildNodeFooterLine(node, childCount, isScopeCenter),
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            metaSize,
            new SolidColorBrush(WithAlpha(MetaColor, alpha)))
        {
            TextAlignment = TextAlignment.Left,
            MaxTextWidth = Math.Max(42, rect.Width - paddingX * 2),
            Trimming = TextTrimming.CharacterEllipsis
        };

        var footerY = rect.Bottom - meta.Height - paddingY;
        context.DrawText(meta, new Point(rect.X + paddingX, footerY));
    }

    private void DrawCenteredMessage(DrawingContext context, string message)
    {
        var text = new FormattedText(message, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Typeface.Default, 15, new SolidColorBrush(Color.Parse("#667085")))
        {
            TextAlignment = TextAlignment.Center,
            MaxTextWidth = Math.Max(40, Bounds.Width),
            Trimming = TextTrimming.CharacterEllipsis
        };

        context.DrawText(text, new Point((Bounds.Width - text.Width) / 2, (Bounds.Height - text.Height) / 2));
    }

    private void BuildHierarchy()
    {
        _parentByChild.Clear();
        _childrenByParent.Clear();
        _rootNodeIds.Clear();

        var hierarchyEdges = _edges
            .Where(IsHierarchyEdge)
            .OrderBy(ResolveHierarchyPriority)
            .ThenBy(edge => edge.From, StringComparer.OrdinalIgnoreCase)
            .ThenBy(edge => edge.To, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var edge in hierarchyEdges)
        {
            if (!_nodeMap.ContainsKey(edge.From) || !_nodeMap.ContainsKey(edge.To))
                continue;

            if (!_parentByChild.TryAdd(edge.To, edge.From))
                continue;

            if (!_childrenByParent.TryGetValue(edge.From, out var children))
            {
                children = [];
                _childrenByParent[edge.From] = children;
            }

            children.Add(edge.To);
        }

        foreach (var node in _nodes)
        {
            if (!_parentByChild.ContainsKey(node.Id))
                _rootNodeIds.Add(node.Id);
        }

        if (_rootNodeIds.Count == 0)
            _rootNodeIds.AddRange(_nodes.Select(node => node.Id));

        foreach (var pair in _childrenByParent)
            pair.Value.Sort(SortNodeIds);

        _rootNodeIds.Sort(SortNodeIds);
    }

    private void RebuildVisibleGraph(bool resetViewport)
    {
        _visibleNodes.Clear();
        _visibleEdges.Clear();
        _layout.Clear();

        var visibleIds = ResolveVisibleNodeIds();
        foreach (var id in visibleIds)
        {
            if (_nodeMap.TryGetValue(id, out var node))
                _visibleNodes.Add(node);
        }

        var visibleSet = new HashSet<string>(_visibleNodes.Select(node => node.Id), StringComparer.OrdinalIgnoreCase);
        var mergedEdges = new Dictionary<string, TopologyEdgeViewModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var edge in _edges)
        {
            var relationKey = ResolveRelationKey(edge);
            if (!ShouldRenderRelation(relationKey))
                continue;

            var from = CollapseToVisibleNode(edge.From, visibleSet);
            var to = CollapseToVisibleNode(edge.To, visibleSet);

            if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
                continue;

            if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
                continue;

            if (IsHierarchyEdge(edge) &&
                (!visibleSet.Contains(from) || !visibleSet.Contains(to)))
            {
                continue;
            }

            var key = $"{relationKey}|{edge.Kind}|{from}|{to}";
            if (mergedEdges.TryGetValue(key, out var existing))
            {
                if (existing.IsComputed && !edge.IsComputed)
                    mergedEdges[key] = existing with { IsComputed = false };
                continue;
            }

            mergedEdges[key] = edge with
            {
                From = from,
                To = to
            };
        }

        _visibleEdges.AddRange(mergedEdges.Values);

        if (_selectedNodeId is not null && !visibleSet.Contains(_selectedNodeId))
            _selectedNodeId = null;

        if (_hoverNodeId is not null && !visibleSet.Contains(_hoverNodeId))
            _hoverNodeId = null;

        RebuildLayout();
        if (resetViewport)
            ResetViewport();

        InvalidateVisual();
    }

    private List<string> ResolveVisibleNodeIds()
    {
        if (_nodes.Count == 0)
            return [];

        if (_viewRootId is null || !_nodeMap.ContainsKey(_viewRootId))
            return [.. _rootNodeIds];

        var children = ResolveScopedChildIds(_viewRootId);
        if (children.Count > 0)
        {
            var ids = new List<string>(children.Count + 1) { _viewRootId };
            ids.AddRange(children.Where(childId => !string.Equals(childId, _viewRootId, StringComparison.OrdinalIgnoreCase)));
            return ids;
        }

        return [_viewRootId];
    }

    private List<string> ResolveScopedChildIds(string parentId)
    {
        if (!_childrenByParent.TryGetValue(parentId, out var children) || children.Count == 0)
            return [];

        var scoped = children
            .Where(childId => _nodeMap.ContainsKey(childId))
            .ToList();

        if (!_nodeMap.TryGetValue(parentId, out var parent))
            return scoped;

        if (!string.Equals(parent.Type, "Project", StringComparison.OrdinalIgnoreCase))
            return scoped;

        var departments = scoped
            .Where(childId =>
                _nodeMap.TryGetValue(childId, out var child) &&
                string.Equals(child.Type, "Department", StringComparison.OrdinalIgnoreCase))
            .ToList();

        return departments.Count > 0 ? departments : scoped;
    }

    private void RebuildLayout()
    {
        if (_visibleNodes.Count == 0)
            return;

        if (TryLayoutScopedParentCenter())
            return;

        var layerGroups = _visibleNodes.GroupBy(ResolveLayer).OrderBy(group => group.Key).ToList();
        var totalHeight = Math.Max(0, (layerGroups.Count - 1) * RowGap);
        var startY = -totalHeight / 2.0;

        for (var layerIndex = 0; layerIndex < layerGroups.Count; layerIndex++)
        {
            var sorted = layerGroups[layerIndex]
                .OrderBy(node => string.IsNullOrWhiteSpace(node.DisciplineLabel) ? "unknown" : node.DisciplineLabel, StringComparer.OrdinalIgnoreCase)
                .ThenBy(node => ResolveTypeOrder(node.Type))
                .ThenBy(node => node.Label, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var totalWidth = Math.Max(0, (sorted.Count - 1) * ColumnGap);
            var startX = -totalWidth / 2.0;
            var rowY = startY + layerIndex * RowGap;

            for (var index = 0; index < sorted.Count; index++)
                _layout[sorted[index].Id] = new Point(startX + index * ColumnGap, rowY);
        }
    }

    private bool TryLayoutScopedParentCenter()
    {
        if (string.IsNullOrWhiteSpace(_viewRootId) || !_nodeMap.ContainsKey(_viewRootId))
            return false;

        if (!_visibleNodes.Any(node => string.Equals(node.Id, _viewRootId, StringComparison.OrdinalIgnoreCase)))
            return false;

        var orbitNodes = _visibleNodes
            .Where(node => !string.Equals(node.Id, _viewRootId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(node => string.IsNullOrWhiteSpace(node.DisciplineLabel) ? "unknown" : node.DisciplineLabel, StringComparer.OrdinalIgnoreCase)
            .ThenBy(node => ResolveTypeOrder(node.Type))
            .ThenBy(node => node.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _layout[_viewRootId] = new Point(0, -188);
        if (orbitNodes.Count == 0)
            return true;

        var consumed = 0;
        var rowIndex = 0;
        while (consumed < orbitNodes.Count)
        {
            var rowCount = Math.Min(orbitNodes.Count - consumed, ScopedRowCapacity);
            var totalWidth = Math.Max(0, (rowCount - 1) * ColumnGap);
            var startX = -totalWidth / 2.0;
            var rowY = 88 + rowIndex * RowGap;

            for (var i = 0; i < rowCount; i++)
            {
                var node = orbitNodes[consumed + i];
                _layout[node.Id] = new Point(startX + i * ColumnGap, rowY);
            }

            consumed += rowCount;
            rowIndex++;
        }

        return true;
    }

    private bool IsPinnedScopeNode(string nodeId)
    {
        return !string.IsNullOrWhiteSpace(_viewRootId) &&
               string.Equals(nodeId, _viewRootId, StringComparison.OrdinalIgnoreCase) &&
               _visibleNodes.Count > 1;
    }

    private string? CollapseToVisibleNode(string nodeId, HashSet<string> visibleSet)
    {
        if (visibleSet.Contains(nodeId))
            return nodeId;

        var current = nodeId;
        var guard = 0;
        while (!string.IsNullOrWhiteSpace(current) && guard < 64)
        {
            if (_parentByChild.TryGetValue(current, out var parentId))
            {
                current = parentId;
                if (visibleSet.Contains(current))
                    return current;
                guard++;
                continue;
            }

            break;
        }

        if (!string.IsNullOrWhiteSpace(_viewRootId) && visibleSet.Contains(_viewRootId))
            return _viewRootId;

        return null;
    }

    private TopologyNodeViewModel? HitTestNode(Point graphPoint)
    {
        foreach (var node in _visibleNodes)
        {
            if (!_layout.TryGetValue(node.Id, out var center))
                continue;

            var rect = BuildNodeRectInGraph(center, node);
            if (rect.Contains(graphPoint))
                return node;
        }

        return null;
    }

    private Point GraphToScreen(Point graphPoint)
    {
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        return new Point(center.X + _panOffset.X + graphPoint.X * _zoom, center.Y + _panOffset.Y + graphPoint.Y * _zoom);
    }

    private Point ScreenToGraph(Point screenPoint)
    {
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        return new Point((screenPoint.X - center.X - _panOffset.X) / _zoom, (screenPoint.Y - center.Y - _panOffset.Y) / _zoom);
    }

    private HashSet<string> BuildConnectedNodes(string focusNodeId)
    {
        var connected = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { focusNodeId };
        foreach (var edge in _visibleEdges)
        {
            if (string.Equals(edge.From, focusNodeId, StringComparison.OrdinalIgnoreCase))
                connected.Add(edge.To);
            if (string.Equals(edge.To, focusNodeId, StringComparison.OrdinalIgnoreCase))
                connected.Add(edge.From);
        }

        return connected;
    }

    private static void DrawArrow(DrawingContext context, Point end, Point control2, Color color)
    {
        var dx = end.X - control2.X;
        var dy = end.Y - control2.Y;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 8)
            return;

        var ux = dx / len;
        var uy = dy / len;
        const double arrowLen = 8;
        const double arrowWidth = 4;

        var baseX = end.X - ux * arrowLen;
        var baseY = end.Y - uy * arrowLen;
        var left = new Point(baseX - uy * arrowWidth, baseY + ux * arrowWidth);
        var right = new Point(baseX + uy * arrowWidth, baseY - ux * arrowWidth);

        var geometry = new StreamGeometry();
        using var g = geometry.Open();
        g.BeginFigure(end, true);
        g.LineTo(left);
        g.LineTo(right);
        g.EndFigure(true);

        context.DrawGeometry(new SolidColorBrush(color), null, geometry);
    }

    private static void DrawDiamond(DrawingContext context, Point start, Point control1, Color color, bool filled)
    {
        var dx = control1.X - start.X;
        var dy = control1.Y - start.Y;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 8)
            return;

        var ux = dx / len;
        var uy = dy / len;
        const double diamondLen = 10;
        const double diamondWidth = 4;

        var p1 = new Point(start.X + ux * diamondLen, start.Y + uy * diamondLen);
        var midX = (start.X + p1.X) / 2;
        var midY = (start.Y + p1.Y) / 2;
        var p2 = new Point(midX - uy * diamondWidth, midY + ux * diamondWidth);
        var p3 = new Point(midX + uy * diamondWidth, midY - ux * diamondWidth);

        var geometry = new StreamGeometry();
        using var g = geometry.Open();
        g.BeginFigure(start, true);
        g.LineTo(p2);
        g.LineTo(p1);
        g.LineTo(p3);
        g.EndFigure(true);

        var fill = filled ? new SolidColorBrush(color) : new SolidColorBrush(Color.Parse("#FFFFFF"));
        context.DrawGeometry(fill, new Pen(new SolidColorBrush(color), 1), geometry);
    }

    private Rect BuildNodeRectInGraph(Point graphCenter, TopologyNodeViewModel node)
    {
        var (width, height) = GetNodeSize(node);
        return new Rect(graphCenter.X - width / 2, graphCenter.Y - height / 2, width, height);
    }

    private Rect BuildNodeRectInScreen(Point graphCenter, TopologyNodeViewModel node)
    {
        var center = GraphToScreen(graphCenter);
        var (graphWidth, graphHeight) = GetNodeSize(node);
        var width = graphWidth * _zoom;
        var height = graphHeight * _zoom;
        return new Rect(center.X - width / 2, center.Y - height / 2, width, height);
    }

    private static (Point start, Point end, bool horizontal) GetAnchors(Rect fromRect, Rect toRect)
    {
        var fromCx = fromRect.X + fromRect.Width / 2;
        var fromCy = fromRect.Y + fromRect.Height / 2;
        var toCx = toRect.X + toRect.Width / 2;
        var toCy = toRect.Y + toRect.Height / 2;
        var dx = toCx - fromCx;
        var dy = toCy - fromCy;

        if (Math.Abs(dx) > Math.Abs(dy))
        {
            return (
                new Point(dx >= 0 ? fromRect.Right : fromRect.X, fromCy),
                new Point(dx >= 0 ? toRect.X : toRect.Right, toCy),
                true);
        }

        return (
            new Point(fromCx, dy >= 0 ? fromRect.Bottom : fromRect.Y),
            new Point(toCx, dy >= 0 ? toRect.Y : toRect.Bottom),
            false);
    }

    private static List<Point> BuildOrthogonalRoute(Point start, Point end, bool horizontal)
    {
        if (Math.Abs(start.X - end.X) < 1 || Math.Abs(start.Y - end.Y) < 1)
            return [start, end];

        var points = new List<Point> { start };
        if (horizontal)
        {
            var midX = start.X + (end.X - start.X) / 2.0;
            points.Add(new Point(midX, start.Y));
            points.Add(new Point(midX, end.Y));
        }
        else
        {
            var midY = start.Y + (end.Y - start.Y) / 2.0;
            points.Add(new Point(start.X, midY));
            points.Add(new Point(end.X, midY));
        }

        points.Add(end);
        return CompactOrthogonalRoute(points);
    }

    private static List<Point> CompactOrthogonalRoute(List<Point> points)
    {
        if (points.Count <= 2)
            return points;

        var result = new List<Point> { points[0] };
        for (var i = 1; i < points.Count; i++)
        {
            var current = points[i];
            var previous = result[^1];
            if (Math.Abs(current.X - previous.X) < 0.01 && Math.Abs(current.Y - previous.Y) < 0.01)
                continue;

            if (result.Count >= 2)
            {
                var beforePrevious = result[^2];
                var sameVertical = Math.Abs(beforePrevious.X - previous.X) < 0.01 &&
                                   Math.Abs(previous.X - current.X) < 0.01;
                var sameHorizontal = Math.Abs(beforePrevious.Y - previous.Y) < 0.01 &&
                                     Math.Abs(previous.Y - current.Y) < 0.01;
                if (sameVertical || sameHorizontal)
                {
                    result[^1] = current;
                    continue;
                }
            }

            result.Add(current);
        }

        return result;
    }

    private static StreamGeometry BuildRoundedOrthogonalGeometry(IReadOnlyList<Point> points, double radius)
    {
        var geometry = new StreamGeometry();
        using var g = geometry.Open();
        g.BeginFigure(points[0], false);

        if (points.Count == 2)
        {
            g.LineTo(points[1]);
            g.EndFigure(false);
            return geometry;
        }

        for (var i = 1; i < points.Count - 1; i++)
        {
            var previous = points[i - 1];
            var current = points[i];
            var next = points[i + 1];

            var incomingLength = Distance(previous, current);
            var outgoingLength = Distance(current, next);
            if (incomingLength < 0.01 || outgoingLength < 0.01)
                continue;

            var cornerRadius = Math.Min(radius, Math.Min(incomingLength, outgoingLength) / 2.0);
            var startCorner = MoveTowards(current, previous, cornerRadius);
            var endCorner = MoveTowards(current, next, cornerRadius);

            g.LineTo(startCorner);
            g.QuadraticBezierTo(current, endCorner);
        }

        g.LineTo(points[^1]);
        g.EndFigure(false);
        return geometry;
    }

    private static double Distance(Point left, Point right)
    {
        var dx = right.X - left.X;
        var dy = right.Y - left.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static Point MoveTowards(Point from, Point to, double distance)
    {
        var length = Distance(from, to);
        if (length < 0.01)
            return from;

        var ratio = distance / length;
        return new Point(
            from.X + (to.X - from.X) * ratio,
            from.Y + (to.Y - from.Y) * ratio);
    }

    private static Color EdgeColor(string relation)
    {
        return relation.ToLowerInvariant() switch
        {
            "dependency" => Color.Parse("#2F6FED"),
            "composition" => Color.Parse("#12B76A"),
            "aggregation" => Color.Parse("#F79009"),
            "collaboration" => Color.Parse("#7A5AF8"),
            "parentchild" => Color.Parse("#98A2B3"),
            _ => Color.Parse("#98A2B3")
        };
    }

    private static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

    private bool IsScopeNode(string nodeId)
    {
        return !string.IsNullOrWhiteSpace(_viewRootId) &&
               string.Equals(nodeId, _viewRootId, StringComparison.OrdinalIgnoreCase);
    }

    private (double Width, double Height) GetNodeSize(TopologyNodeViewModel node)
    {
        return IsScopeNode(node.Id)
            ? (ScopeNodeWidth, ScopeNodeHeight)
            : (NodeWidth, NodeHeight);
    }

    private static Rect ExpandRect(Rect rect, double horizontal, double vertical)
    {
        return new Rect(
            rect.X - horizontal,
            rect.Y - vertical,
            rect.Width + horizontal * 2,
            rect.Height + vertical * 2);
    }

    private static Color NodeAccent(TopologyNodeViewModel node)
    {
        return node.Type.ToLowerInvariant() switch
        {
            "project" => Color.Parse("#2F6FED"),
            "department" => Color.Parse("#12B76A"),
            "team" => Color.Parse("#F79009"),
            _ => Color.Parse("#98A2B3")
        };
    }

    private static Color NodeFill(TopologyNodeViewModel node, bool isScopeCenter)
    {
        var color = NodeFill(node);
        if (!isScopeCenter)
            return color;

        return Color.FromArgb(
            color.A,
            (byte)Math.Min(255, color.R + 12),
            (byte)Math.Min(255, color.G + 14),
            (byte)Math.Min(255, color.B + 18));
    }

    private static string ResolveNodeBadge(TopologyNodeViewModel node)
    {
        return node.Type.ToLowerInvariant() switch
        {
            "project" => "项目",
            "department" => "部门",
            "team" => "团队",
            _ => "模块"
        };
    }

    private void DrawActionPill(
        DrawingContext context,
        string text,
        double anchorX,
        double topY,
        double fontSize,
        byte alpha,
        Color accent,
        bool filled,
        bool alignRight)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            fontSize,
            new SolidColorBrush(WithAlpha(LabelColor, alpha)));
        var width = Math.Max(40, formatted.Width + 14);
        var height = Math.Max(18, formatted.Height + 6);
        var x = alignRight ? anchorX - width : anchorX;
        var rect = new Rect(x, topY, width, height);
        var fill = filled
            ? new SolidColorBrush(Color.FromArgb(Math.Min(alpha, (byte)20), accent.R, accent.G, accent.B))
            : new SolidColorBrush(Color.FromArgb(Math.Min(alpha, (byte)18), SurfaceStrokeColor.R, SurfaceStrokeColor.G, SurfaceStrokeColor.B));
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(Math.Min(alpha, (byte)84), accent.R, accent.G, accent.B)), 1);

        context.DrawRectangle(fill, pen, rect, 7 * _zoom);
        context.DrawText(
            formatted,
            new Point(
                rect.X + (rect.Width - formatted.Width) / 2,
                rect.Y + (rect.Height - formatted.Height) / 2 - 0.5));
    }

    private static Color NodeFill(TopologyNodeViewModel node)
    {
        return node.Type.ToLowerInvariant() switch
        {
            "project" => Color.Parse("#FFFFFF"),
            "department" => Color.Parse("#FFFFFF"),
            "team" => Color.Parse("#FFFFFF"),
            _ => Color.Parse("#FFFFFF")
        };
    }

    private string BuildNodeFooterLine(TopologyNodeViewModel node, int childCount, bool isScopeCenter)
    {
        var line = $"deps {node.DependencyCount}";
        if (_hasLayerData)
            line = $"L{ResolveLayer(node)} · {line}";
        if (!isScopeCenter && childCount > 0)
            line = $"{line} · {childCount} children";
        return line;
    }

    private static bool IsHierarchyEdge(TopologyEdgeViewModel edge)
    {
        var relation = (edge.Relation ?? string.Empty).Trim().ToLowerInvariant();
        return relation is "containment" or "parentchild";
    }

    private static int ResolveHierarchyPriority(TopologyEdgeViewModel edge)
    {
        var relation = (edge.Relation ?? string.Empty).Trim().ToLowerInvariant();
        if (relation == "parentchild")
            return 0;

        var kind = (edge.Kind ?? string.Empty).Trim().ToLowerInvariant();
        if (kind == "composition")
            return 1;
        if (kind == "aggregation")
            return 2;
        return edge.IsComputed ? 3 : 1;
    }

    private string ResolveRelationKey(TopologyEdgeViewModel edge)
    {
        var relation = (edge.Relation ?? string.Empty).Trim().ToLowerInvariant();
        if (relation == "dependency")
            return "dependency";
        if (relation == "collaboration")
            return "collaboration";
        if (relation == "parentchild")
            return "parentchild";
        if (relation is "composition" or "aggregation")
            return relation;
        if (relation == "containment")
        {
            var kind = (edge.Kind ?? string.Empty).Trim().ToLowerInvariant();
            if (kind is "composition" or "aggregation")
                return kind;
            return edge.IsComputed ? "aggregation" : "composition";
        }

        return "dependency";
    }

    private bool ShouldRenderRelation(string relationKey)
    {
        return relationKey switch
        {
            "dependency" => _showDependency,
            "composition" => _showComposition || _showParentChild,
            "aggregation" => _showAggregation || _showParentChild,
            "parentchild" => _showParentChild,
            "collaboration" => _showCollaboration,
            _ => true
        };
    }

    private IReadOnlyList<string> BuildScopeTrailIds()
    {
        if (string.IsNullOrWhiteSpace(_viewRootId))
            return Array.Empty<string>();

        var trail = new List<string>();
        var current = _viewRootId;
        var guard = 0;
        while (!string.IsNullOrWhiteSpace(current) && guard < 64)
        {
            trail.Insert(0, current);
            current = _parentByChild.TryGetValue(current, out var parentId) ? parentId : null;
            guard++;
        }

        return trail;
    }

    private int SortNodeIds(string leftId, string rightId)
    {
        var left = _nodeMap[leftId];
        var right = _nodeMap[rightId];
        var disciplineCompare = string.Compare(left.DisciplineLabel, right.DisciplineLabel, StringComparison.OrdinalIgnoreCase);
        if (disciplineCompare != 0)
            return disciplineCompare;

        var typeCompare = ResolveTypeOrder(left.Type).CompareTo(ResolveTypeOrder(right.Type));
        if (typeCompare != 0)
            return typeCompare;

        return string.Compare(left.Label, right.Label, StringComparison.OrdinalIgnoreCase);
    }

    private void ResetViewport()
    {
        _panOffset = new Vector(0, 0);
        _zoom = 1.0;
    }

    private static int ResolveLayer(TopologyNodeViewModel node)
    {
        if (node.ComputedLayer.HasValue)
            return node.ComputedLayer.Value;

        return node.Type.ToLowerInvariant() switch
        {
            "project" => 0,
            "department" => 0,
            "technical" => 1,
            "gateway" => 1,
            "team" => 2,
            _ => 1
        };
    }

    private static int ResolveTypeOrder(string type)
    {
        return type.ToLowerInvariant() switch
        {
            "project" => 0,
            "department" => 1,
            "technical" => 2,
            "gateway" => 3,
            "team" => 4,
            _ => 9
        };
    }
}
