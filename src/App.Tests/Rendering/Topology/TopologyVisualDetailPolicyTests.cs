using Dna.App.Desktop.Topology;
using Xunit;

namespace App.Tests;

public sealed class TopologyVisualDetailPolicyTests
{
    [Theory]
    [InlineData(1.0, true, true, true, true, true)]
    [InlineData(0.80, true, true, true, true, true)]
    [InlineData(0.70, true, true, false, false, false)]
    [InlineData(0.56, true, true, false, false, false)]
    [InlineData(0.50, false, false, false, false, false)]
    public void TopologyVisualDetailPolicy_ShouldResolveExpectedLevel(
        double zoom,
        bool showDots,
        bool showBadge,
        bool showMeta,
        bool showActionPill,
        bool showEdgeMarkers)
    {
        var level = TopologyVisualDetailPolicy.Resolve(zoom);

        Assert.Equal(showDots, level.ShowBackgroundDots);
        Assert.Equal(showBadge, level.ShowBadge);
        Assert.Equal(showMeta, level.ShowMeta);
        Assert.Equal(showActionPill, level.ShowActionPill);
        Assert.Equal(showEdgeMarkers, level.ShowEdgeMarkers);
    }
}
