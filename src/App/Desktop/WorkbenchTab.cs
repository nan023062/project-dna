using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Dna.App.Desktop;

public sealed class WorkbenchTab : TabItem
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<WorkbenchTab, string?>(nameof(Title), string.Empty);

    public static readonly StyledProperty<string?> IconGlyphProperty =
        AvaloniaProperty.Register<WorkbenchTab, string?>(nameof(IconGlyph), string.Empty);

    public WorkbenchTab()
    {
        Header = CreateHeader();
        ToolTip.SetTip(this, Title ?? string.Empty);
    }

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? IconGlyph
    {
        get => GetValue(IconGlyphProperty);
        set => SetValue(IconGlyphProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TitleProperty || change.Property == IconGlyphProperty)
        {
            Header = CreateHeader();
            ToolTip.SetTip(this, Title ?? string.Empty);
        }
    }

    private Control CreateHeader()
    {
        var icon = CreateIcon(IconGlyph);
        if (icon is not null)
            return icon;

        return new Border
        {
            Child = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(IconGlyph) ? "?" : IconGlyph,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Classes = { "workspaceTabGlyph" }
            },
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private static Control? CreateIcon(string? iconKey)
    {
        if (string.IsNullOrWhiteSpace(iconKey))
            return null;

        return new PathIcon
        {
            Width = 18,
            Height = 18,
            Data = Geometry.Parse(iconKey.Trim().ToLowerInvariant() switch
            {
                "knowledge" => "F1 M3,2 L11.5,2 C12.8807,2 14,3.11929 14,4.5 L14,13 L5.5,13 C4.67157,13 4,13.6716 4,14.5 C4,14.7761 3.77614,15 3.5,15 C3.22386,15 3,14.7761 3,14.5 z M4,3.5 L4,12.3378 C4.45558,12.1208 4.9651,12 5.5,12 L13,12 L13,4.5 C13,3.67157 12.3284,3 11.5,3 L4.5,3 C4.22386,3 4,3.22386 4,3.5 z",
                "workspace" => "F1 M1.75,3.5 C1.75,2.5335 2.5335,1.75 3.5,1.75 L6.2,1.75 C6.66413,1.75 7.10925,1.93437 7.43744,2.26256 L8.175,3 L12.5,3 C13.4665,3 14.25,3.7835 14.25,4.75 L14.25,11.5 C14.25,12.4665 13.4665,13.25 12.5,13.25 L3.5,13.25 C2.5335,13.25 1.75,12.4665 1.75,11.5 z M3.5,4 C3.08579,4 2.75,4.33579 2.75,4.75 L2.75,11.5 C2.75,11.9142 3.08579,12.25 3.5,12.25 L12.5,12.25 C12.9142,12.25 13.25,11.9142 13.25,11.5 L13.25,4.75 C13.25,4.33579 12.9142,4 12.5,4 z",
                "memory" => "F1 M8,1.75 C4.54822,1.75 1.75,4.54822 1.75,8 C1.75,11.4518 4.54822,14.25 8,14.25 C11.4518,14.25 14.25,11.4518 14.25,8 C14.25,4.54822 11.4518,1.75 8,1.75 z M8,2.75 C10.8995,2.75 13.25,5.1005 13.25,8 C13.25,10.8995 10.8995,13.25 8,13.25 C5.1005,13.25 2.75,10.8995 2.75,8 C2.75,5.1005 5.1005,2.75 8,2.75 z M7.5,4.5 L8.5,4.5 L8.5,7.58579 L10.7071,9.79289 L10,10.5 L7.5,8 z",
                "tools" => "F1 M6.25,1.75 L9.75,1.75 L10.15,3.55 C10.4945,3.68186 10.8218,3.85357 11.125,4.06066 L12.85,3.35 L14.6,6.4 L13.2,7.6 C13.2332,7.86384 13.25,8.13087 13.25,8.4 C13.25,8.66913 13.2332,8.93616 13.2,9.2 L14.6,10.4 L12.85,13.45 L11.125,12.7393 C10.8218,12.9464 10.4945,13.1181 10.15,13.25 L9.75,15.05 L6.25,15.05 L5.85,13.25 C5.50547,13.1181 5.17818,12.9464 4.875,12.7393 L3.15,13.45 L1.4,10.4 L2.8,9.2 C2.76684,8.93616 2.75,8.66913 2.75,8.4 C2.75,8.13087 2.76684,7.86384 2.8,7.6 L1.4,6.4 L3.15,3.35 L4.875,4.06066 C5.17818,3.85357 5.50547,3.68186 5.85,3.55 z M8,5.65 C6.48071,5.65 5.25,6.88071 5.25,8.4 C5.25,9.91929 6.48071,11.15 8,11.15 C9.51929,11.15 10.75,9.91929 10.75,8.4 C10.75,6.88071 9.51929,5.65 8,5.65 z",
                "settings" => "F1 M6.25,1.75 L9.75,1.75 L10.15,3.55 C10.4945,3.68186 10.8218,3.85357 11.125,4.06066 L12.85,3.35 L14.6,6.4 L13.2,7.6 C13.2332,7.86384 13.25,8.13087 13.25,8.4 C13.25,8.66913 13.2332,8.93616 13.2,9.2 L14.6,10.4 L12.85,13.45 L11.125,12.7393 C10.8218,12.9464 10.4945,13.1181 10.15,13.25 L9.75,15.05 L6.25,15.05 L5.85,13.25 C5.50547,13.1181 5.17818,12.9464 4.875,12.7393 L3.15,13.45 L1.4,10.4 L2.8,9.2 C2.76684,8.93616 2.75,8.66913 2.75,8.4 C2.75,8.13087 2.76684,7.86384 2.8,7.6 L1.4,6.4 L3.15,3.35 L4.875,4.06066 C5.17818,3.85357 5.50547,3.68186 5.85,3.55 z M8,5.65 C6.48071,5.65 5.25,6.88071 5.25,8.4 C5.25,9.91929 6.48071,11.15 8,11.15 C9.51929,11.15 10.75,9.91929 10.75,8.4 C10.75,6.88071 9.51929,5.65 8,5.65 z",
                _ => string.Empty
            }),
            Classes = { "workspaceTabIcon" }
        };
    }
}
