using Avalonia.Controls;

namespace Dna.App.Desktop;

public sealed class WorkbenchTabLayout : TabControl
{
    public WorkbenchTabLayout()
    {
        Classes.Add("workspaceTabs");
        TabStripPlacement = Dock.Top;
    }
}
