using Dna.Client.Services;
using Xunit;

namespace Client.Tests;

public sealed class ClientBootstrapTests
{
    [Fact]
    public void ResolveWorkspaceRoot_ShouldPreferCliArgument()
    {
        var workspaceRoot = CreateTempDirectory();

        var resolved = ClientBootstrap.ResolveWorkspaceRoot(["--workspace-root", workspaceRoot]);

        Assert.Equal(Path.GetFullPath(workspaceRoot), resolved);
    }

    [Fact]
    public void ResolveWorkspaceConfigPath_ShouldPreferCliArgument()
    {
        var configPath = Path.Combine(CreateTempDirectory(), "client-workspaces.json");

        var resolved = ClientBootstrap.ResolveWorkspaceConfigPath(["--workspace-config", configPath]);

        Assert.Equal(Path.GetFullPath(configPath), resolved);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "dna-client-bootstrap-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
