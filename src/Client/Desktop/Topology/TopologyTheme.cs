using Avalonia.Media;

namespace Dna.Client.Desktop.Topology;

public sealed class TopologyTheme
{
    public static TopologyTheme Default { get; } = new();

    public Color Background { get; init; } = Color.Parse("#F8FAFC");
    public Color Grid { get; init; } = Color.Parse("#CBD5E1");
    public Color Border { get; init; } = Color.Parse("#D0D5DD");
    public Color Label { get; init; } = Color.Parse("#101828");
    public Color Meta { get; init; } = Color.Parse("#667085");
    public Color SurfaceStroke { get; init; } = Color.Parse("#FFFFFF");
    public Color ScopeBorder { get; init; } = Color.Parse("#2E90FA");
    public Color SelectedBorder { get; init; } = Color.Parse("#2F6FED");
    public Color Collaboration { get; init; } = Color.Parse("#0EA5E9");
    public Color Dependency { get; init; } = Color.Parse("#2F6FED");
    public Color Composition { get; init; } = Color.Parse("#12B76A");
    public Color Aggregation { get; init; } = Color.Parse("#F79009");
    public Color ParentChild { get; init; } = Color.Parse("#98A2B3");

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
        var color = Color.Parse("#FFFFFF");
        if (!isScopeCenter)
            return color;

        return Color.FromArgb(
            color.A,
            (byte)Math.Min(255, color.R + 12),
            (byte)Math.Min(255, color.G + 14),
            (byte)Math.Min(255, color.B + 18));
    }
}
