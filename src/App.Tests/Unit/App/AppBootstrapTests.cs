using Dna.App.Services;
using Xunit;

namespace App.Tests;

public sealed class AppBootstrapTests
{
    [Fact]
    public void NormalizeUrl_ShouldTrimWhitespaceAndTrailingSlash()
    {
        var resolved = AppBootstrap.NormalizeUrl("  http://localhost:5051/  ");

        Assert.Equal("http://localhost:5051", resolved);
    }

    [Fact]
    public void NormalizeUrl_ShouldPreservePathWithoutTrailingSlash()
    {
        var resolved = AppBootstrap.NormalizeUrl("https://dna.example.com/api");

        Assert.Equal("https://dna.example.com/api", resolved);
    }
}
