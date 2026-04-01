using Dna.Client.Interfaces.Cli;
using Xunit;

namespace Client.Tests;

public sealed class ClientLaunchModeTests
{
    [Fact]
    public void Parse_ShouldEnterCliMode_WhenFirstArgIsCli()
    {
        var result = ClientLaunchModeParser.Parse(["cli", "status"]);

        Assert.Equal(ClientLaunchModeKind.Cli, result.Kind);
        Assert.Equal(["status"], result.Args);
    }

    [Fact]
    public void Parse_ShouldEnterCliMode_WhenFirstArgIsCliAlias()
    {
        var result = ClientLaunchModeParser.Parse(["--cli", "help"]);

        Assert.Equal(ClientLaunchModeKind.Cli, result.Kind);
        Assert.Equal(["help"], result.Args);
    }

    [Fact]
    public void Parse_ShouldTreatDesktopAsDefaultMode()
    {
        var result = ClientLaunchModeParser.Parse(["desktop", "--no-splash"]);

        Assert.Equal(ClientLaunchModeKind.Desktop, result.Kind);
        Assert.Equal(["--no-splash"], result.Args);
    }

    [Fact]
    public void ClientCliSettings_ShouldHonorExplicitUrlOverride()
    {
        var settings = ClientCliSettings.Parse(["--url", "http://localhost:6060/", "status"]);

        Assert.Equal("http://localhost:6060", settings.BaseUrl);
        Assert.Equal(["status"], settings.Args);
    }
}
