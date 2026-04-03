using Xunit;

namespace App.Tests;

public sealed class WebUiAssetSmokeTests
{
    [Fact]
    public void AppDesktopShell_ShouldContainWorkspaceAndToolingEntrances()
    {
        var axaml = ReadRepoFile("App", "Desktop", "MainWindow.axaml");

        Assert.Contains("ProjectLoadPanel", axaml);
        Assert.Contains("WorkspacePanel", axaml);
        Assert.Contains("RecentProjectsListBox", axaml);
        Assert.Contains("ConnectionStatusText", axaml);
        Assert.Contains("TopologyGraph", axaml);
        Assert.Contains("MemoryListBox", axaml);
        Assert.Contains("McpToolListBox", axaml);
        Assert.Contains("ChatMessagesHost", axaml);
        Assert.Contains("ChatInputBox", axaml);
        Assert.Contains("ChatProviderBox", axaml);
    }

    [Fact]
    public void AppBrowserWorkbench_ShouldBeRemoved()
    {
        var srcRoot = FindSrcRoot();
        var appIndex = Path.Combine(srcRoot, "App", "wwwroot", "index.html");

        Assert.False(File.Exists(appIndex), $"App browser workbench should not exist anymore: {appIndex}");
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
                Directory.Exists(Path.Combine(srcPath, "App")))
            {
                return srcPath;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate repository src directory.");
    }
}
