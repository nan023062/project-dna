using System.Globalization;
using Avalonia;
using Avalonia.Media;

namespace Dna.App.Desktop.Topology;

public sealed record TopologyEdgeGraphRoute(
    Rect FromRect,
    Rect ToRect,
    IReadOnlyList<Point> GraphPoints);

public static class TopologyRenderGeometry
{
    public static Rect BuildNodeRectInGraph(Point graphCenter, bool isScopeCenter, TopologyLayoutOptions options)
    {
        var (width, height) = GetNodeSize(isScopeCenter, options);
        return new Rect(graphCenter.X - width / 2, graphCenter.Y - height / 2, width, height);
    }

    public static Rect BuildNodeRectInScreen(
        Size boundsSize,
        TopologyViewportState viewport,
        Point graphCenter,
        bool isScopeCenter,
        TopologyLayoutOptions options)
    {
        var center = viewport.GraphToScreen(boundsSize, graphCenter);
        var (graphWidth, graphHeight) = GetNodeSize(isScopeCenter, options);
        var width = graphWidth * viewport.Zoom;
        var height = graphHeight * viewport.Zoom;
        return new Rect(center.X - width / 2, center.Y - height / 2, width, height);
    }

    public static TopologyEdgeGraphRoute BuildGraphRoute(
        Point fromGraphCenter,
        Point toGraphCenter,
        bool fromScopeCenter,
        bool toScopeCenter,
        TopologyLayoutOptions options)
    {
        var fromRect = BuildNodeRectInGraph(fromGraphCenter, fromScopeCenter, options);
        var toRect = BuildNodeRectInGraph(toGraphCenter, toScopeCenter, options);
        var (start, end, horizontal) = GetAnchors(fromRect, toRect);
        var route = BuildOrthogonalRoute(start, end, horizontal);
        return new TopologyEdgeGraphRoute(fromRect, toRect, route);
    }

    public static List<Point> TransformRouteToScreen(
        Size boundsSize,
        TopologyViewportState viewport,
        IReadOnlyList<Point> graphPoints)
    {
        var points = new List<Point>(graphPoints.Count);
        foreach (var point in graphPoints)
            points.Add(viewport.GraphToScreen(boundsSize, point));

        return points;
    }

    public static StreamGeometry BuildRoundedOrthogonalGeometry(IReadOnlyList<Point> points, double radius)
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

    public static Rect ExpandRect(Rect rect, double horizontal, double vertical)
    {
        return new Rect(
            rect.X - horizontal,
            rect.Y - vertical,
            rect.Width + horizontal * 2,
            rect.Height + vertical * 2);
    }

    public static Color WithAlpha(Color color, byte alpha)
        => Color.FromArgb(alpha, color.R, color.G, color.B);

    private static (double Width, double Height) GetNodeSize(bool isScopeCenter, TopologyLayoutOptions options)
    {
        return isScopeCenter
            ? (options.ScopeNodeWidth, options.ScopeNodeHeight)
            : (options.NodeWidth, options.NodeHeight);
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
}

public sealed class TopologyEdgeRouteCache
{
    private const int MaxEntries = 512;
    private readonly Dictionary<TopologyEdgeRouteCacheKey, TopologyEdgeGraphRoute> _cache = [];

    public int Count => _cache.Count;

    public TopologyEdgeGraphRoute GetOrCreate(
        Point fromGraphCenter,
        Point toGraphCenter,
        bool fromScopeCenter,
        bool toScopeCenter,
        TopologyLayoutOptions options)
    {
        var key = new TopologyEdgeRouteCacheKey(
            Quantize(fromGraphCenter.X),
            Quantize(fromGraphCenter.Y),
            Quantize(toGraphCenter.X),
            Quantize(toGraphCenter.Y),
            fromScopeCenter,
            toScopeCenter);

        if (_cache.TryGetValue(key, out var cached))
            return cached;

        if (_cache.Count >= MaxEntries)
            _cache.Clear();

        var route = TopologyRenderGeometry.BuildGraphRoute(fromGraphCenter, toGraphCenter, fromScopeCenter, toScopeCenter, options);
        _cache[key] = route;
        return route;
    }

    private static double Quantize(double value) => Math.Round(value, 2);

    private readonly record struct TopologyEdgeRouteCacheKey(
        double FromX,
        double FromY,
        double ToX,
        double ToY,
        bool FromScopeCenter,
        bool ToScopeCenter);
}

public sealed class TopologyFormattedTextCache
{
    private const int MaxEntries = 1024;
    private readonly Dictionary<TopologyFormattedTextCacheKey, FormattedText> _cache = [];

    public int Count => _cache.Count;

    public FormattedText GetOrCreate(
        string text,
        double fontSize,
        Color color,
        double maxTextWidth,
        double? maxTextHeight,
        TextAlignment alignment,
        TextTrimming trimming)
    {
        var key = new TopologyFormattedTextCacheKey(
            text,
            Quantize(fontSize),
            color.ToUInt32(),
            Quantize(maxTextWidth),
            maxTextHeight.HasValue ? Quantize(maxTextHeight.Value) : -1,
            alignment,
            trimming,
            CultureInfo.CurrentUICulture.Name);

        if (_cache.TryGetValue(key, out var cached))
            return cached;

        if (_cache.Count >= MaxEntries)
            _cache.Clear();

        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            key.FontSize,
            new SolidColorBrush(color))
        {
            TextAlignment = alignment,
            MaxTextWidth = Math.Max(1, key.MaxTextWidth),
            MaxTextHeight = key.MaxTextHeight > 0 ? key.MaxTextHeight : double.PositiveInfinity,
            Trimming = trimming
        };

        _cache[key] = formatted;
        return formatted;
    }

    private static double Quantize(double value) => Math.Round(value, 1);

    private readonly record struct TopologyFormattedTextCacheKey(
        string Text,
        double FontSize,
        uint Color,
        double MaxTextWidth,
        double MaxTextHeight,
        TextAlignment Alignment,
        TextTrimming Trimming,
        string CultureName);
}

public sealed class TopologyViewportCuller
{
    public TopologyRenderList Cull(
        TopologyRenderList renderList,
        Rect bounds,
        TopologyViewportState viewport,
        TopologyLayoutOptions options)
    {
        if (renderList.Nodes.Count == 0)
            return renderList;

        var paddedBounds = TopologyRenderGeometry.ExpandRect(bounds, 120, 120);
        var visibleNodes = renderList.Nodes
            .Where(node =>
            {
                var rect = TopologyRenderGeometry.BuildNodeRectInScreen(
                    bounds.Size,
                    viewport,
                    node.GraphPoint,
                    node.IsScopeCenter,
                    options);
                return rect.Intersects(paddedBounds);
            })
            .ToList();

        if (visibleNodes.Count == renderList.Nodes.Count)
            return renderList;

        var visibleNodeIds = visibleNodes
            .Select(node => node.Node.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var visibleEdges = renderList.Edges
            .Where(edge =>
                visibleNodeIds.Contains(edge.FromNode.Id) ||
                visibleNodeIds.Contains(edge.ToNode.Id))
            .ToList();

        return new TopologyRenderList(visibleNodes, visibleEdges, renderList.FocusNodeId);
    }
}
