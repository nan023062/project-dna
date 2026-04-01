using Dna.Client.Services;
using Xunit;

namespace Client.Tests;

public sealed class ClientBootstrapTests
{
    [Fact]
    public void NormalizeUrl_ShouldTrimWhitespaceAndTrailingSlash()
    {
        var resolved = ClientBootstrap.NormalizeUrl("  http://localhost:5051/  ");

        Assert.Equal("http://localhost:5051", resolved);
    }

    [Fact]
    public void NormalizeUrl_ShouldPreservePathWithoutTrailingSlash()
    {
        var resolved = ClientBootstrap.NormalizeUrl("https://dna.example.com/api");

        Assert.Equal("https://dna.example.com/api", resolved);
    }
}
