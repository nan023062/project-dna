using Xunit;

namespace Client.Tests;

public sealed class WebUiAssetSmokeTests
{
    [Fact]
    public void ClientDesktopShell_ShouldContainWorkspaceAndToolingEntrances()
    {
        var axaml = ReadRepoFile("Client", "Desktop", "MainWindow.axaml");

        Assert.Contains("ProjectLoadPanel", axaml);
        Assert.Contains("WorkspacePanel", axaml);
        Assert.Contains("RecentProjectsListBox", axaml);
        Assert.Contains("ConnectionStatusText", axaml);
        Assert.Contains("TopologyGraph", axaml);
        Assert.Contains("MemoryListBox", axaml);
        Assert.Contains("McpToolListBox", axaml);
    }

    [Fact]
    public void ClientBrowserWorkbench_ShouldBeRemoved()
    {
        var srcRoot = FindSrcRoot();
        var clientIndex = Path.Combine(srcRoot, "Client", "wwwroot", "index.html");

        Assert.False(File.Exists(clientIndex), $"Client browser workbench should not exist anymore: {clientIndex}");
    }

    [Fact]
    public void ServerIndex_ShouldContainOverviewAndConnectionAdminPanels()
    {
        var html = ReadRepoFile("Server", "wwwroot", "index.html");

        Assert.Contains("panelOverview", html);
        Assert.Contains("overviewServiceState", html);
        Assert.Contains("panelUsers", html);
        Assert.Contains("whitelistList", html);
        Assert.Contains("whitelistEditorForm", html);
        Assert.Contains("panelMemoryMgmt", html);
        Assert.Contains("chatPanel", html);
    }

    [Fact]
    public void SharedUserAdminAssets_ShouldExistForClientAndServer()
    {
        var script = ReadRepoFile("Dna.Web.Shared", "wwwroot", "js", "panels", "user-admin-common.js");
        var css = ReadRepoFile("Dna.Web.Shared", "wwwroot", "css", "user-admin.css");

        Assert.Contains("createUserAdminController", script);
        Assert.Contains("user-admin-page", css);
    }

    [Fact]
    public void ServerChatPanel_ShouldContainSessionResumeAndEditActions()
    {
        var script = ReadRepoFile("Server", "wwwroot", "js", "chat", "chat-panel.js");

        Assert.Contains("showSessionList", script);
        Assert.Contains("continueChatFromLimit", script);
        Assert.Contains("keepEdit", script);
        Assert.Contains("undoEdit", script);
    }

    private static string ReadRepoFile(params string[] relativeSegments)
    {
        var srcRoot = FindSrcRoot();
        var path = Path.Combine([srcRoot, .. relativeSegments]);
        Assert.True(File.Exists(path), $"Expected file was not found: {path}");
        return File.ReadAllText(path);
    }

    private static string FindSrcRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            var srcPath = Path.Combine(current.FullName, "src");
            if (Directory.Exists(srcPath) &&
                Directory.Exists(Path.Combine(srcPath, "Client")) &&
                Directory.Exists(Path.Combine(srcPath, "Server")))
            {
                return srcPath;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate repository src directory.");
    }
}
