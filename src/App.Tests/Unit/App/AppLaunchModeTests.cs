using Dna.ExternalAgent.Cli;
using Xunit;

namespace App.Tests;

public sealed class AppLaunchModeTests
{
    [Fact]
    public void Parse_ShouldEnterCliMode_WhenFirstArgIsCli()
    {
        var result = AppLaunchModeParser.Parse(["cli", "status"]);

        Assert.Equal(AppLaunchModeKind.Cli, result.Kind);
        Assert.Equal(["status"], result.Args);
    }

    [Fact]
    public void Parse_ShouldEnterCliMode_WhenFirstArgIsCliAlias()
    {
        var result = AppLaunchModeParser.Parse(["--cli", "help"]);

        Assert.Equal(AppLaunchModeKind.Cli, result.Kind);
        Assert.Equal(["help"], result.Args);
    }

    [Fact]
    public void Parse_ShouldTreatDesktopAsDefaultMode()
    {
        var result = AppLaunchModeParser.Parse(["desktop", "--no-splash"]);

        Assert.Equal(AppLaunchModeKind.Desktop, result.Kind);
        Assert.Equal(["--no-splash"], result.Args);
    }

    [Fact]
    public void ExternalAgentCliSettings_ShouldHonorExplicitUrlOverride()
    {
        var settings = ExternalAgentCliSettings.Parse(["--url", "http://localhost:6060/", "status"]);

        Assert.Equal("http://localhost:6060", settings.BaseUrl);
        Assert.Equal(["status"], settings.Args);
    }
}
