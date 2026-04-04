namespace Dna.App.Desktop.ViewModels;

public sealed record RecentProjectItemViewModel(DesktopRecentProjectEntry Entry)
{
    public string ProjectRoot => Entry.ProjectRoot;

    public string Title
    {
        get
        {
            var lastOpened = Entry.LastOpenedAtUtc.ToLocalTime().ToString("MM-dd HH:mm");
            return $"{Entry.ProjectName}  [{lastOpened}]";
        }
    }

    public override string ToString() => $"{Title}\n{ProjectRoot}";
}
