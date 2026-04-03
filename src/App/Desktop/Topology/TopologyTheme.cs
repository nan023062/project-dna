using Avalonia.Media;

namespace Dna.App.Desktop.Topology;

public sealed class TopologyTheme
{
    public static TopologyTheme Default { get; } = new();

    public Color Background { get; init; } = Color.Parse("#1E1E1E");
    public Color Grid { get; init; } = Color.Parse("#3A3D41");
    public Color Border { get; init; } = Color.Parse("#3C3C3C");
    public Color Label { get; init; } = Color.Parse("#F3F3F3");
    public Color Meta { get; init; } = Color.Parse("#A7A7A7");
    public Color SurfaceStroke { get; init; } = Color.Parse("#2A2D2E");
    public Color ScopeBorder { get; init; } = Color.Parse("#4FC1FF");
    public Color SelectedBorder { get; init; } = Color.Parse("#0E639C");
    public Color Collaboration { get; init; } = Color.Parse("#C586C0");
    public Color Dependency { get; init; } = Color.Parse("#4FC1FF");
    public Color Composition { get; init; } = Color.Parse("#73C991");
    public Color Aggregation { get; init; } = Color.Parse("#D7BA7D");
    public Color ParentChild { get; init; } = Color.Parse("#7F848E");

    public Color ResolveEdgeColor(string relationKey)
    {
        return relationKey.ToLowerInvariant() switch
        {
            "dependency" => Dependency,
            "composition" => Composition,
            "aggregation" => Aggregation,
            "collaboration" => Collaboration,
            "parentchild" => ParentChild,
            _ => ParentChild
        };
    }

    public Color ResolveNodeAccent(TopologyNodeViewModel node)
    {
        return node.Type.ToLowerInvariant() switch
        {
            "project" => Dependency,
            "department" => Composition,
            "team" => Aggregation,
            _ => ParentChild
        };
    }

    public Color ResolveNodeFill(TopologyNodeViewModel node, bool isScopeCenter)
    {
        var color = Color.Parse("#252526");
        if (!isScopeCenter)
            return color;

        return Color.FromArgb(
            color.A,
            (byte)Math.Min(255, color.R + 10),
            (byte)Math.Min(255, color.G + 10),
            (byte)Math.Min(255, color.B + 12));
    }
}
