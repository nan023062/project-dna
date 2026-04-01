namespace Dna.Client.Desktop.Topology;

public sealed record TopologyVisualDetailLevel(
    bool ShowBackgroundDots,
    bool ShowBadge,
    bool ShowMeta,
    bool ShowActionPill,
    bool ShowEdgeMarkers);

public static class TopologyVisualDetailPolicy
{
    public static TopologyVisualDetailLevel Resolve(double zoom)
    {
        if (zoom < 0.56)
        {
            return new TopologyVisualDetailLevel(
                ShowBackgroundDots: false,
                ShowBadge: false,
                ShowMeta: false,
                ShowActionPill: false,
                ShowEdgeMarkers: false);
        }

        if (zoom < 0.78)
        {
            return new TopologyVisualDetailLevel(
                ShowBackgroundDots: true,
                ShowBadge: true,
                ShowMeta: false,
                ShowActionPill: false,
                ShowEdgeMarkers: false);
        }

        return new TopologyVisualDetailLevel(
            ShowBackgroundDots: true,
            ShowBadge: true,
            ShowMeta: true,
            ShowActionPill: true,
            ShowEdgeMarkers: true);
    }
}
