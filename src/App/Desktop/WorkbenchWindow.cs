using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;

namespace Dna.App.Desktop;

public sealed class WorkbenchWindow : UserControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<WorkbenchWindow, string?>(nameof(Title), string.Empty);

    public static readonly StyledProperty<object?> HeaderRightContentProperty =
        AvaloniaProperty.Register<WorkbenchWindow, object?>(nameof(HeaderRightContent));

    public static readonly StyledProperty<object?> WindowContentProperty =
        AvaloniaProperty.Register<WorkbenchWindow, object?>(nameof(WindowContent));

    private readonly TextBlock _titleBlock;
    private readonly ContentPresenter _headerRightPresenter;
    private readonly ContentPresenter _contentPresenter;

    public WorkbenchWindow()
    {
        _titleBlock = new TextBlock
        {
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };
        _titleBlock.Classes.Add("sectionTitle");

        _headerRightPresenter = new ContentPresenter();
        Grid.SetColumn(_headerRightPresenter, 1);

        var headerGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 8
        };
        headerGrid.Children.Add(_titleBlock);
        headerGrid.Children.Add(_headerRightPresenter);

        var headerBorder = new Border
        {
            Child = headerGrid
        };
        headerBorder.Classes.Add("panelHeader");

        _contentPresenter = new ContentPresenter();
        Grid.SetRow(_contentPresenter, 1);

        var rootGrid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*")
        };
        rootGrid.Children.Add(headerBorder);
        rootGrid.Children.Add(_contentPresenter);

        var shellBorder = new Border
        {
            Child = rootGrid,
            Padding = new Thickness(0)
        };
        shellBorder.Classes.Add("panelSurface");

        Content = shellBorder;
        ApplyState();
    }

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public object? HeaderRightContent
    {
        get => GetValue(HeaderRightContentProperty);
        set => SetValue(HeaderRightContentProperty, value);
    }

    public object? WindowContent
    {
        get => GetValue(WindowContentProperty);
        set => SetValue(WindowContentProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TitleProperty ||
            change.Property == HeaderRightContentProperty ||
            change.Property == WindowContentProperty)
        {
            ApplyState();
        }
    }

    private void ApplyState()
    {
        _titleBlock.Text = Title ?? string.Empty;
        _headerRightPresenter.Content = HeaderRightContent;
        _contentPresenter.Content = WindowContent;
    }
}
