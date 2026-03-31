using Xunit;

namespace Client.Tests;

public sealed class WebUiAssetSmokeTests
{
    [Fact]
    public void ClientIndex_ShouldContainWorkspaceKnowledgeAndChatEntryPoints()
    {
        var html = ReadRepoFile("Client", "wwwroot", "index.html");

        Assert.Contains("workspace-tab", html);
        Assert.Contains("panelAccount", html);
        Assert.Contains("clientAccountRoot", html);
        Assert.Contains("panelMemory", html);
        Assert.Contains("chatPanel", html);
        Assert.Contains("chatInput", html);
        Assert.Contains("llmSettingsOverlay", html);
        Assert.Contains("marked.min.js", html);
    }

    [Fact]
    public void ServerIndex_ShouldContainAdminWorkbenchAndReviewQueue()
    {
        var html = ReadRepoFile("Server", "wwwroot", "index.html");

        Assert.Contains("reviewQueue", html);
        Assert.Contains("panelUsers", html);
        Assert.Contains("userAdminRoot", html);
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
