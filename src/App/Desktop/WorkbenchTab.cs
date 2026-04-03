using Avalonia;
using Avalonia.Controls;

namespace Dna.App.Desktop;

public sealed class WorkbenchTab : TabItem
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<WorkbenchTab, string?>(nameof(Title), string.Empty);

    public WorkbenchTab()
    {
        Header = Title ?? string.Empty;
    }

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TitleProperty)
            Header = Title ?? string.Empty;
    }
}
