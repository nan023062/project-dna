using System.Globalization;
using Avalonia;
using Avalonia.Media;

namespace Dna.App.Desktop.Topology;

public sealed class AvaloniaTopologyRenderer
{
    private const string EmptyScopeMessage = "\u5F53\u524D\u5C42\u7EA7\u6682\u65E0\u53EF\u663E\u793A\u8282\u70B9";
    private const string CurrentScopeLabel = "\u5F53\u524D\u4F5C\u7528\u57DF";

    private readonly TopologyTheme _theme;
    private readonly TopologyFormattedTextCache _textCache = new();
    private readonly TopologyEdgeRouteCache _routeCache = new();
    private readonly TopologyViewportCuller _viewportCuller = new();

    public AvaloniaTopologyRenderer(TopologyTheme? theme = null)
    {
        _theme = theme ?? TopologyTheme.Default;
    }

    public void Render(
        DrawingContext context,
        Rect bounds,
        TopologyRenderList renderList,
        TopologyViewportState viewport,
        TopologyLayoutOptions options,
        string? viewRootId,
        bool hasLayerData)
    {
        var culledRenderList = _viewportCuller.Cull(renderList, bounds, viewport, options);
        var detailLevel = TopologyVisualDetailPolicy.Resolve(viewport.Zoom);

        context.FillRectangle(new SolidColorBrush(_theme.Background), bounds);
        if (detailLevel.ShowBackgroundDots)
            DrawGrid(context, bounds);

        if (culledRenderList.Nodes.Count == 0)
        {
            DrawCenteredMessage(context, bounds, EmptyScopeMessage);
            return;
        }

        DrawEdges(context, bounds, culledRenderList, viewport, options, viewRootId, detailLevel);
        DrawNodes(context, bounds, culledRenderList, viewport, options, hasLayerData, detailLevel);
    }

    private void DrawGrid(DrawingContext context, Rect bounds)
    {
        var brush = new SolidColorBrush(Color.FromArgb(34, _theme.Grid.R, _theme.Grid.G, _theme.Grid.B));
        const double step = 44;
        const double radius = 1.1;

        for (var y = step / 2.0; y < bounds.Height; y += step)
        {
            for (var x = step / 2.0; x < bounds.Width; x += step)
                context.DrawEllipse(brush, null, new Point(x, y), radius, radius);
        }
    }

    private void DrawEdges(
        DrawingContext context,
        Rect bounds,
        TopologyRenderList renderList,
        TopologyViewportState viewport,
        TopologyLayoutOptions options,
        string? viewRootId,
        TopologyVisualDetailLevel detailLevel)
    {
        foreach (var item in renderList.Edges)
        {
            var fromScopeCenter = string.Equals(item.FromNode.Id, viewRootId, StringComparison.OrdinalIgnoreCase);
            var toScopeCenter = string.Equals(item.ToNode.Id, viewRootId, StringComparison.OrdinalIgnoreCase);
            var route = _routeCache.GetOrCreate(item.FromCenter, item.ToCenter, fromScopeCenter, toScopeCenter, options);
            var screenRoute = TopologyRenderGeometry.TransformRouteToScreen(bounds.Size, viewport, route.GraphPoints);

            var color = _theme.ResolveEdgeColor(item.RelationKey);
            var thickness = item.Edge.IsComputed ? 0.95 : 1.35;
            DashStyle? dashStyle = null;
            if (string.Equals(item.RelationKey, "collaboration", StringComparison.OrdinalIgnoreCase))
                dashStyle = new DashStyle([6, 4], 0);
            else if (string.Equals(item.RelationKey, "aggregation", StringComparison.OrdinalIgnoreCase))
                dashStyle = new DashStyle([4, 4], 0);
            else if (string.Equals(item.RelationKey, "parentchild", StringComparison.OrdinalIgnoreCase))
                dashStyle = new DashStyle([3, 6], 0);

            if (renderList.FocusNodeId is not null)
            {
                if (!item.IsConnected)
                    color = TopologyRenderGeometry.WithAlpha(color, 80);
                else
                    thickness += 0.45;
            }

            var geometry = TopologyRenderGeometry.BuildRoundedOrthogonalGeometry(screenRoute, 14);
            context.DrawGeometry(null, new Pen(new SolidColorBrush(color), thickness) { DashStyle = dashStyle }, geometry);

            if (detailLevel.ShowEdgeMarkers)
            {
                if (string.Equals(item.RelationKey, "composition", StringComparison.OrdinalIgnoreCase))
                    DrawDiamond(context, screenRoute[0], screenRoute[1], color, filled: true);
                else if (string.Equals(item.RelationKey, "aggregation", StringComparison.OrdinalIgnoreCase))
                    DrawDiamond(context, screenRoute[0], screenRoute[1], color, filled: false);

                DrawArrow(context, screenRoute[^1], screenRoute[^2], color);
            }
        }
    }

    private void DrawNodes(
        DrawingContext context,
        Rect bounds,
        TopologyRenderList renderList,
        TopologyViewportState viewport,
        TopologyLayoutOptions options,
        bool hasLayerData,
        TopologyVisualDetailLevel detailLevel)
    {
        foreach (var item in renderList.Nodes)
        {
            var zoom = viewport.Zoom;
            var rect = TopologyRenderGeometry.BuildNodeRectInScreen(bounds.Size, viewport, item.GraphPoint, item.IsScopeCenter, options);
            var alpha = item.IsConnected ? (byte)255 : (byte)85;
            var accent = TopologyRenderGeometry.WithAlpha(_theme.ResolveNodeAccent(item.Node), alpha);

            var fill = TopologyRenderGeometry.WithAlpha(_theme.ResolveNodeFill(item.Node, item.IsScopeCenter), alpha);
            var border = item.IsSelected
                ? _theme.SelectedBorder
                : item.IsScopeCenter
                    ? _theme.ScopeBorder
                    : _theme.Border;
            border = TopologyRenderGeometry.WithAlpha(border, alpha);

            if (item.IsSelected || item.IsHovered)
            {
                var halo = TopologyRenderGeometry.ExpandRect(rect, 4 * zoom, 4 * zoom);
                var haloColor = item.IsSelected
                    ? Color.FromArgb(Math.Min(alpha, (byte)28), 47, 111, 237)
                    : Color.FromArgb(Math.Min(alpha, (byte)20), 152, 162, 179);
                context.DrawRectangle(new SolidColorBrush(haloColor), null, halo, 16 * zoom);
            }

            context.DrawRectangle(
                new SolidColorBrush(fill),
                new Pen(new SolidColorBrush(border), item.IsSelected ? 2.1 : item.IsScopeCenter ? 1.6 : 1.0),
                rect,
                14 * zoom);

            DrawNodeTexts(context, item.Node, rect, zoom, alpha, accent, item.ChildCount, item.IsScopeCenter, hasLayerData, detailLevel);
        }
    }

    private void DrawNodeTexts(
        DrawingContext context,
        TopologyNodeViewModel node,
        Rect rect,
        double zoom,
        byte alpha,
        Color accent,
        int childCount,
        bool isScopeCenter,
        bool hasLayerData,
        TopologyVisualDetailLevel detailLevel)
    {
        var paddingX = Math.Clamp(14 * zoom, 12, 20);
        var paddingY = Math.Clamp(12 * zoom, 10, 18);
        var badgeFontSize = Math.Clamp(8.6 * zoom, 7, 11);
        var titleSize = detailLevel.ShowMeta
            ? Math.Clamp((isScopeCenter ? 14.2 : 13.0) * zoom, 10, 18)
            : Math.Clamp((isScopeCenter ? 13.2 : 12.2) * zoom, 9, 15);
        var metaSize = Math.Clamp(9.0 * zoom, 8, 12);
        var actionFontSize = Math.Clamp(8.2 * zoom, 7, 11);
        var badgeText = ResolveNodeBadge(node);

        var labelColor = TopologyRenderGeometry.WithAlpha(_theme.Label, alpha);
        var titleTop = rect.Y + paddingY;

        if (detailLevel.ShowBadge)
        {
            var badge = _textCache.GetOrCreate(
                badgeText,
                badgeFontSize,
                labelColor,
                Math.Max(36, rect.Width - paddingX * 2),
                null,
                TextAlignment.Left,
                TextTrimming.None);
            var badgeHeight = Math.Max(16, badge.Height + 4);
            var badgeWidth = Math.Max(36, badge.Width + 10);
            var badgeRect = new Rect(rect.X + paddingX, rect.Y + paddingY, badgeWidth, badgeHeight);
            context.DrawRectangle(
                new SolidColorBrush(Color.FromArgb(Math.Min(alpha, (byte)16), _theme.SurfaceStroke.R, _theme.SurfaceStroke.G, _theme.SurfaceStroke.B)),
                new Pen(new SolidColorBrush(Color.FromArgb(Math.Min(alpha, (byte)46), _theme.Border.R, _theme.Border.G, _theme.Border.B)), 1),
                badgeRect,
                8 * zoom);
            context.DrawText(
                badge,
                new Point(
                    badgeRect.X + (badgeRect.Width - badge.Width) / 2,
                    badgeRect.Y + (badgeRect.Height - badge.Height) / 2 - 0.5));

            titleTop = badgeRect.Bottom + 9 * zoom;

            if (isScopeCenter && detailLevel.ShowActionPill)
            {
                DrawActionPill(
                    context,
                    CurrentScopeLabel,
                    rect.Right - paddingX,
                    rect.Y + paddingY,
                    actionFontSize,
                    zoom,
                    alpha,
                    accent,
                    filled: false,
                    alignRight: true);
            }
        }
        var title = _textCache.GetOrCreate(
            node.Label,
            titleSize,
            labelColor,
            Math.Max(48, rect.Width - paddingX * 2),
            Math.Max(28, rect.Height * 0.34),
            TextAlignment.Left,
            TextTrimming.CharacterEllipsis);

        context.DrawText(title, new Point(rect.X + paddingX, titleTop));

        if (detailLevel.ShowMeta)
        {
            var meta = _textCache.GetOrCreate(
                BuildNodeFooterLine(node, childCount, isScopeCenter, hasLayerData),
                metaSize,
                TopologyRenderGeometry.WithAlpha(_theme.Meta, alpha),
                Math.Max(42, rect.Width - paddingX * 2),
                null,
                TextAlignment.Left,
                TextTrimming.CharacterEllipsis);

            var footerY = rect.Bottom - meta.Height - paddingY;
            context.DrawText(meta, new Point(rect.X + paddingX, footerY));
        }
    }

    private void DrawCenteredMessage(DrawingContext context, Rect bounds, string message)
    {
        var text = _textCache.GetOrCreate(
            message,
            15,
            _theme.Meta,
            Math.Max(40, bounds.Width),
            null,
            TextAlignment.Center,
            TextTrimming.CharacterEllipsis);

        context.DrawText(text, new Point((bounds.Width - text.Width) / 2, (bounds.Height - text.Height) / 2));
    }

    private void DrawActionPill(
        DrawingContext context,
        string text,
        double anchorX,
        double topY,
        double fontSize,
        double zoom,
        byte alpha,
        Color accent,
        bool filled,
        bool alignRight)
    {
        var formatted = _textCache.GetOrCreate(
            text,
            fontSize,
            TopologyRenderGeometry.WithAlpha(_theme.Label, alpha),
            400,
            null,
            TextAlignment.Left,
            TextTrimming.None);
        var width = Math.Max(40, formatted.Width + 14);
        var height = Math.Max(18, formatted.Height + 6);
        var x = alignRight ? anchorX - width : anchorX;
        var rect = new Rect(x, topY, width, height);
        var fill = filled
            ? new SolidColorBrush(Color.FromArgb(Math.Min(alpha, (byte)20), accent.R, accent.G, accent.B))
            : new SolidColorBrush(Color.FromArgb(Math.Min(alpha, (byte)18), _theme.SurfaceStroke.R, _theme.SurfaceStroke.G, _theme.SurfaceStroke.B));
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(Math.Min(alpha, (byte)84), accent.R, accent.G, accent.B)), 1);

        context.DrawRectangle(fill, pen, rect, 7 * zoom);
        context.DrawText(
            formatted,
            new Point(
                rect.X + (rect.Width - formatted.Width) / 2,
                rect.Y + (rect.Height - formatted.Height) / 2 - 0.5));
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

    private static string ResolveNodeBadge(TopologyNodeViewModel node)
    {
        return node.Type.ToLowerInvariant() switch
        {
            "project" => "\u9879\u76EE",
            "department" => "\u90E8\u95E8",
            "team" => "\u56E2\u961F",
            _ => "\u6A21\u5757"
        };
    }

    private static string BuildNodeFooterLine(
        TopologyNodeViewModel node,
        int childCount,
        bool isScopeCenter,
        bool hasLayerData)
    {
        var line = $"deps {node.DependencyCount}";
        if (hasLayerData)
            line = $"L{TopologyGraphSemantics.ResolveLayer(node)} | {line}";
        if (!isScopeCenter && childCount > 0)
            line = $"{line} | {childCount} children";
        return line;
    }
}
