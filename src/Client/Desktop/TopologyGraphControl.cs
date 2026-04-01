using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using System.Globalization;

namespace Dna.Client.Desktop;

public sealed record TopologyNodeViewModel(
    string Id,
    string Label,
    string Type,
    string TypeLabel,
    string Discipline,
    string DisciplineLabel,
    int DependencyCount,
    string Summary,
    int? ComputedLayer);

public sealed record TopologyEdgeViewModel(
    string From,
    string To,
    string Relation,
    bool IsComputed);

public sealed class TopologyGraphControl : Control
{
    private const double NodeWidth = 186;
    private const double NodeHeight = 74;
    private const double ColumnGap = 260;
    private const double RowGap = 170;

    private static readonly Color BackgroundColor = Color.Parse("#0B1020");
    private static readonly Color GridColor = Color.Parse("#17233A");
    private static readonly Color BorderColor = Color.Parse("#24314D");
    private static readonly Color LabelColor = Color.Parse("#E2E8F0");
    private static readonly Color MetaColor = Color.Parse("#94A3B8");

    private IReadOnlyList<TopologyNodeViewModel> _nodes = [];
    private IReadOnlyList<TopologyEdgeViewModel> _edges = [];
    private readonly Dictionary<string, Point> _layout = new(StringComparer.OrdinalIgnoreCase);

    private string? _selectedNodeId;
    private string? _hoverNodeId;
    private bool _isPanning;
    private string? _draggingNodeId;
    private Point _dragStartGraphPoint;
    private Point _dragNodeOriginCenter;
    private Point _panOrigin;
    private Vector _panOffset = new(0, 0);
    private double _zoom = 1.0;
    private bool _showDependency = true;
    private bool _showComposition = true;
    private bool _showAggregation = true;
    private bool _showParentChild = true;
    private bool _showCollaboration = true;
    private bool _hasLayerData;

    public event Action<TopologyNodeViewModel>? NodeSelected;

    public void SetTopology(IReadOnlyList<TopologyNodeViewModel> nodes, IReadOnlyList<TopologyEdgeViewModel> edges)
    {
        _nodes = nodes;
        _edges = edges;
        _hasLayerData = _nodes.Any(n => n.ComputedLayer.HasValue);
        if (_selectedNodeId is not null && _nodes.All(n => !string.Equals(n.Id, _selectedNodeId, StringComparison.OrdinalIgnoreCase)))
            _selectedNodeId = null;

        RebuildLayout();
        InvalidateVisual();
    }

    public void ClearTopology()
    {
        _nodes = [];
        _edges = [];
        _layout.Clear();
        _selectedNodeId = null;
        _hasLayerData = false;
        InvalidateVisual();
    }

    public void SelectNode(string? nodeId)
    {
        _selectedNodeId = nodeId;
        InvalidateVisual();
    }

    public void SetRelationFilter(
        bool dependency,
        bool composition,
        bool aggregation,
        bool parentChild,
        bool collaboration)
    {
        _showDependency = dependency;
        _showComposition = composition;
        _showAggregation = aggregation;
        _showParentChild = parentChild;
        _showCollaboration = collaboration;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        var position = e.GetPosition(this);
        var graphPoint = ScreenToGraph(position);
        var hit = HitTestNode(graphPoint);
        if (hit is not null)
        {
            _selectedNodeId = hit.Id;
            NodeSelected?.Invoke(hit);
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
        _panOrigin = position;
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
            _layout[_draggingNodeId] = new Point(
                _dragNodeOriginCenter.X + delta.X,
                _dragNodeOriginCenter.Y + delta.Y);

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

        if (_nodes.Count == 0)
        {
            DrawCenteredMessage(context, "暂无拓扑数据");
            return;
        }

        DrawEdges(context);
        DrawNodes(context);
    }

    private void DrawGrid(DrawingContext context)
    {
        var pen = new Pen(new SolidColorBrush(GridColor), 1);
        const double step = 48;

        for (var x = 0.0; x < Bounds.Width; x += step)
            context.DrawLine(pen, new Point(x, 0), new Point(x, Bounds.Height));

        for (var y = 0.0; y < Bounds.Height; y += step)
            context.DrawLine(pen, new Point(0, y), new Point(Bounds.Width, y));
    }

    private void DrawEdges(DrawingContext context)
    {
        var focusNodeId = _selectedNodeId ?? _hoverNodeId;
        foreach (var edge in _edges)
        {
            var relationKey = ResolveRelationKey(edge);
            if (!ShouldRenderRelation(relationKey))
                continue;

            if (!_layout.TryGetValue(edge.From, out var fromGraphCenter) || !_layout.TryGetValue(edge.To, out var toGraphCenter))
                continue;

            var fromRect = BuildNodeRectInScreen(fromGraphCenter);
            var toRect = BuildNodeRectInScreen(toGraphCenter);
            var (start, end, horizontal) = GetAnchors(fromRect, toRect);
            var (control1, control2) = BuildBezierControls(start, end, horizontal);

            var color = EdgeColor(relationKey);
            var thickness = edge.IsComputed ? 1.2 : 1.8;
            DashStyle? dashStyle = null;

            if (string.Equals(relationKey, "collaboration", StringComparison.OrdinalIgnoreCase))
                dashStyle = new DashStyle([8, 4], 0);
            else if (string.Equals(relationKey, "aggregation", StringComparison.OrdinalIgnoreCase))
                dashStyle = new DashStyle([6, 4], 0);

            if (focusNodeId is not null)
            {
                var connected = string.Equals(edge.From, focusNodeId, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(edge.To, focusNodeId, StringComparison.OrdinalIgnoreCase);
                if (!connected)
                    color = Color.FromArgb(80, color.R, color.G, color.B);
                else
                    thickness += 0.8;
            }

            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                g.BeginFigure(start, false);
                g.CubicBezierTo(control1, control2, end);
                g.EndFigure(false);
            }

            context.DrawGeometry(
                null,
                new Pen(new SolidColorBrush(color), thickness) { DashStyle = dashStyle },
                geometry);

            if (string.Equals(relationKey, "composition", StringComparison.OrdinalIgnoreCase))
                DrawDiamond(context, start, control1, color, filled: true);
            else if (string.Equals(relationKey, "aggregation", StringComparison.OrdinalIgnoreCase))
                DrawDiamond(context, start, control1, color, filled: false);

            DrawArrow(context, end, control2, color);
        }
    }

    private static void DrawArrow(DrawingContext context, Point end, Point control2, Color color)
    {
        // 贝塞尔在 t=1 处的切线方向：end - control2
        var dx = end.X - control2.X;
        var dy = end.Y - control2.Y;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 8)
            return;

        var ux = dx / len;
        var uy = dy / len;
        var arrowLen = 8;
        var arrowWidth = 4;

        var tip = end;
        var baseX = end.X - ux * arrowLen;
        var baseY = end.Y - uy * arrowLen;

        var left = new Point(baseX - uy * arrowWidth, baseY + ux * arrowWidth);
        var right = new Point(baseX + uy * arrowWidth, baseY - ux * arrowWidth);

        var geometry = new StreamGeometry();
        using var g = geometry.Open();
        g.BeginFigure(tip, true);
        g.LineTo(left);
        g.LineTo(right);
        g.EndFigure(true);

        context.DrawGeometry(new SolidColorBrush(color), null, geometry);
    }

    private static void DrawDiamond(DrawingContext context, Point start, Point control1, Color color, bool filled)
    {
        // 贝塞尔在 t=0 处切线方向：control1 - start
        var dx = control1.X - start.X;
        var dy = control1.Y - start.Y;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 8)
            return;

        var ux = dx / len;
        var uy = dy / len;
        const double diamondLen = 10;
        const double diamondWidth = 4;

        var p0 = start;
        var p1 = new Point(start.X + ux * diamondLen, start.Y + uy * diamondLen);
        var midX = (p0.X + p1.X) / 2;
        var midY = (p0.Y + p1.Y) / 2;
        var p2 = new Point(midX - uy * diamondWidth, midY + ux * diamondWidth);
        var p3 = new Point(midX + uy * diamondWidth, midY - ux * diamondWidth);

        var geometry = new StreamGeometry();
        using var g = geometry.Open();
        g.BeginFigure(p0, true);
        g.LineTo(p2);
        g.LineTo(p1);
        g.LineTo(p3);
        g.EndFigure(true);

        var stroke = new Pen(new SolidColorBrush(color), 1);
        var fill = filled
            ? new SolidColorBrush(color)
            : new SolidColorBrush(Color.Parse("#0B1020"));

        context.DrawGeometry(fill, stroke, geometry);
    }

    private Rect BuildNodeRectInScreen(Point graphCenter)
    {
        var center = GraphToScreen(graphCenter);
        var width = NodeWidth * _zoom;
        var height = NodeHeight * _zoom;
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

    private static (Point c1, Point c2) BuildBezierControls(Point start, Point end, bool horizontal)
    {
        if (horizontal)
        {
            var dx = end.X - start.X;
            var c = Math.Clamp(Math.Abs(dx) * 0.45, 36, 160);
            var dir = Math.Sign(dx);
            if (dir == 0)
                dir = 1;

            return (
                new Point(start.X + dir * c, start.Y),
                new Point(end.X - dir * c, end.Y));
        }

        var dy = end.Y - start.Y;
        var cv = Math.Clamp(Math.Abs(dy) * 0.45, 36, 160);
        var vdir = Math.Sign(dy);
        if (vdir == 0)
            vdir = 1;

        return (
            new Point(start.X, start.Y + vdir * cv),
            new Point(end.X, end.Y - vdir * cv));
    }

    private void DrawNodes(DrawingContext context)
    {
        var focusNodeId = _selectedNodeId ?? _hoverNodeId;
        var connectedNodes = focusNodeId is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : BuildConnectedNodes(focusNodeId);

        foreach (var node in _nodes)
        {
            if (!_layout.TryGetValue(node.Id, out var graphPoint))
                continue;

            var center = GraphToScreen(graphPoint);
            var width = NodeWidth * _zoom;
            var height = NodeHeight * _zoom;
            var rect = new Rect(center.X - width / 2, center.Y - height / 2, width, height);

            var isSelected = string.Equals(_selectedNodeId, node.Id, StringComparison.OrdinalIgnoreCase);
            var isFocusNode = focusNodeId is not null &&
                              string.Equals(focusNodeId, node.Id, StringComparison.OrdinalIgnoreCase);
            var isConnected = focusNodeId is null ||
                              isFocusNode ||
                              connectedNodes.Contains(node.Id);
            var alpha = isConnected ? (byte)255 : (byte)85;

            var fill = NodeFill(node);
            var border = isSelected ? Color.Parse("#4ADE80") : BorderColor;
            fill = WithAlpha(fill, alpha);
            border = WithAlpha(border, alpha);

            context.DrawRectangle(new SolidColorBrush(fill), new Pen(new SolidColorBrush(border), isSelected ? 2.2 : 1.0), rect, 10 * _zoom);

            var titleSize = Math.Clamp(12 * _zoom, 9, 18);
            var metaSize = Math.Clamp(10 * _zoom, 8, 14);

            var title = new FormattedText(
                node.Label,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                Typeface.Default,
                titleSize,
                new SolidColorBrush(WithAlpha(LabelColor, alpha)))
            {
                TextAlignment = TextAlignment.Center,
                MaxTextWidth = Math.Max(20, rect.Width - 12),
                MaxTextHeight = Math.Max(18, rect.Height - 24),
                Trimming = TextTrimming.CharacterEllipsis
            };

            var meta = new FormattedText(
                BuildNodeMetaLine(node),
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                Typeface.Default,
                metaSize,
                new SolidColorBrush(WithAlpha(MetaColor, alpha)))
            {
                TextAlignment = TextAlignment.Center,
                MaxTextWidth = Math.Max(20, rect.Width - 12),
                Trimming = TextTrimming.CharacterEllipsis
            };

            var titlePos = new Point(rect.X + (rect.Width - title.Width) / 2, rect.Y + 10 * _zoom);
            var metaPos = new Point(rect.X + (rect.Width - meta.Width) / 2, rect.Bottom - meta.Height - 8 * _zoom);

            context.DrawText(title, titlePos);
            context.DrawText(meta, metaPos);
        }
    }

    private void DrawCenteredMessage(DrawingContext context, string message)
    {
        var text = new FormattedText(
            message,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            15,
            new SolidColorBrush(Color.Parse("#94A3B8")))
        {
            TextAlignment = TextAlignment.Center,
            MaxTextWidth = Math.Max(40, Bounds.Width),
            Trimming = TextTrimming.CharacterEllipsis
        };

        context.DrawText(text, new Point((Bounds.Width - text.Width) / 2, (Bounds.Height - text.Height) / 2));
    }

    private void RebuildLayout()
    {
        _layout.Clear();
        if (_nodes.Count == 0)
            return;

        var layerGroups = _nodes
            .GroupBy(ResolveLayer)
            .OrderBy(g => g.Key)
            .ToList();

        foreach (var layerGroup in layerGroups)
        {
            var sorted = layerGroup
                .OrderBy(n => string.IsNullOrWhiteSpace(n.DisciplineLabel) ? "unknown" : n.DisciplineLabel, StringComparer.OrdinalIgnoreCase)
                .ThenBy(n => ResolveTypeOrder(n.Type))
                .ThenBy(n => n.Label, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var startX = -((sorted.Count - 1) * ColumnGap) / 2.0;
            var rowY = layerGroup.Key * RowGap;
            for (var i = 0; i < sorted.Count; i++)
            {
                var point = new Point(startX + i * ColumnGap, rowY);
                _layout[sorted[i].Id] = point;
            }
        }
    }

    private TopologyNodeViewModel? HitTestNode(Point graphPoint)
    {
        foreach (var node in _nodes)
        {
            if (!_layout.TryGetValue(node.Id, out var center))
                continue;

            var rect = new Rect(
                center.X - NodeWidth / 2,
                center.Y - NodeHeight / 2,
                NodeWidth,
                NodeHeight);

            if (rect.Contains(graphPoint))
                return node;
        }

        return null;
    }

    private Point GraphToScreen(Point graphPoint)
    {
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        return new Point(
            center.X + _panOffset.X + graphPoint.X * _zoom,
            center.Y + _panOffset.Y + graphPoint.Y * _zoom);
    }

    private Point ScreenToGraph(Point screenPoint)
    {
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        return new Point(
            (screenPoint.X - center.X - _panOffset.X) / _zoom,
            (screenPoint.Y - center.Y - _panOffset.Y) / _zoom);
    }

    private static Color EdgeColor(string relation)
    {
        return relation.ToLowerInvariant() switch
        {
            "dependency" => Color.Parse("#4ADE80"),
            "composition" => Color.Parse("#F59E0B"),
            "aggregation" => Color.Parse("#FBBF24"),
            "collaboration" => Color.Parse("#60A5FA"),
            "parentchild" => Color.Parse("#A78BFA"),
            _ => Color.Parse("#64748B")
        };
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
        if (relation == "composition" || relation == "aggregation")
            return relation;

        if (relation == "containment")
            return edge.IsComputed ? "aggregation" : "composition";

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

    private HashSet<string> BuildConnectedNodes(string focusNodeId)
    {
        var connected = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { focusNodeId };

        foreach (var edge in _edges)
        {
            var relationKey = ResolveRelationKey(edge);
            if (!ShouldRenderRelation(relationKey))
                continue;

            if (string.Equals(edge.From, focusNodeId, StringComparison.OrdinalIgnoreCase))
                connected.Add(edge.To);

            if (string.Equals(edge.To, focusNodeId, StringComparison.OrdinalIgnoreCase))
                connected.Add(edge.From);
        }

        return connected;
    }

    private static Color WithAlpha(Color color, byte alpha)
        => Color.FromArgb(alpha, color.R, color.G, color.B);

    private static Color NodeFill(TopologyNodeViewModel node)
    {
        if (string.Equals(node.Type, "Team", StringComparison.OrdinalIgnoreCase))
            return Color.Parse("#162236");

        if (string.Equals(node.Type, "Technical", StringComparison.OrdinalIgnoreCase))
            return Color.Parse("#172A2E");

        return Color.Parse("#1A2234");
    }

    private string BuildNodeMetaLine(TopologyNodeViewModel node)
    {
        var line = $"{node.TypeLabel} · deps {node.DependencyCount}";
        if (_hasLayerData)
        {
            var layer = ResolveLayer(node);
            line = $"L{layer} · {line}";
        }

        return line;
    }

    private static int ResolveLayer(TopologyNodeViewModel node)
    {
        if (node.ComputedLayer.HasValue)
            return node.ComputedLayer.Value;

        return node.Type.ToLowerInvariant() switch
        {
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
            "department" => 0,
            "technical" => 1,
            "gateway" => 2,
            "team" => 3,
            _ => 9
        };
    }
}
