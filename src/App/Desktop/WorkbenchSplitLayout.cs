using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Layout;

namespace Dna.App.Desktop;

public sealed class WorkbenchSplitLayout : UserControl
{
    public static readonly StyledProperty<object?> FirstProperty =
        AvaloniaProperty.Register<WorkbenchSplitLayout, object?>(nameof(First));

    public static readonly StyledProperty<object?> SecondProperty =
        AvaloniaProperty.Register<WorkbenchSplitLayout, object?>(nameof(Second));

    public static readonly StyledProperty<Orientation> OrientationProperty =
        AvaloniaProperty.Register<WorkbenchSplitLayout, Orientation>(nameof(Orientation), Orientation.Horizontal);

    public static readonly StyledProperty<GridLength> PrimaryLengthProperty =
        AvaloniaProperty.Register<WorkbenchSplitLayout, GridLength>(
            nameof(PrimaryLength),
            new GridLength(1, GridUnitType.Star));

    public static readonly StyledProperty<double> SplitterThicknessProperty =
        AvaloniaProperty.Register<WorkbenchSplitLayout, double>(nameof(SplitterThickness), 6d);

    private readonly Grid _rootGrid;
    private readonly ContentPresenter _firstPresenter;
    private readonly ContentPresenter _secondPresenter;
    private readonly GridSplitter _splitter;

    public WorkbenchSplitLayout()
    {
        _rootGrid = new Grid();
        _firstPresenter = new ContentPresenter();
        _secondPresenter = new ContentPresenter();
        _splitter = new GridSplitter
        {
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
            ShowsPreview = false
        };
        _splitter.Classes.Add("shellSplitter");

        Content = _rootGrid;
        ApplyLayout();
    }

    public object? First
    {
        get => GetValue(FirstProperty);
        set => SetValue(FirstProperty, value);
    }

    public object? Second
    {
        get => GetValue(SecondProperty);
        set => SetValue(SecondProperty, value);
    }

    public Orientation Orientation
    {
        get => GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    public GridLength PrimaryLength
    {
        get => GetValue(PrimaryLengthProperty);
        set => SetValue(PrimaryLengthProperty, value);
    }

    public double SplitterThickness
    {
        get => GetValue(SplitterThicknessProperty);
        set => SetValue(SplitterThicknessProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == FirstProperty ||
            change.Property == SecondProperty ||
            change.Property == OrientationProperty ||
            change.Property == PrimaryLengthProperty ||
            change.Property == SplitterThicknessProperty)
        {
            ApplyLayout();
        }
    }

    private void ApplyLayout()
    {
        _rootGrid.Children.Clear();
        _rootGrid.RowDefinitions.Clear();
        _rootGrid.ColumnDefinitions.Clear();
        _firstPresenter.Content = First;
        _secondPresenter.Content = Second;

        if (Orientation == Avalonia.Layout.Orientation.Horizontal)
        {
            _rootGrid.ColumnDefinitions.Add(new ColumnDefinition(PrimaryLength.Value, PrimaryLength.GridUnitType));
            _rootGrid.ColumnDefinitions.Add(new ColumnDefinition(SplitterThickness, GridUnitType.Pixel));
            _rootGrid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));

            Grid.SetColumn(_firstPresenter, 0);
            Grid.SetColumn(_splitter, 1);
            Grid.SetColumn(_secondPresenter, 2);

            _splitter.ResizeDirection = GridResizeDirection.Columns;
            _splitter.ResizeBehavior = GridResizeBehavior.PreviousAndNext;
        }
        else
        {
            _rootGrid.RowDefinitions.Add(new RowDefinition(PrimaryLength.Value, PrimaryLength.GridUnitType));
            _rootGrid.RowDefinitions.Add(new RowDefinition(SplitterThickness, GridUnitType.Pixel));
            _rootGrid.RowDefinitions.Add(new RowDefinition(1, GridUnitType.Star));

            Grid.SetRow(_firstPresenter, 0);
            Grid.SetRow(_splitter, 1);
            Grid.SetRow(_secondPresenter, 2);

            _splitter.ResizeDirection = GridResizeDirection.Rows;
            _splitter.ResizeBehavior = GridResizeBehavior.PreviousAndNext;
        }

        _rootGrid.Children.Add(_firstPresenter);
        _rootGrid.Children.Add(_splitter);
        _rootGrid.Children.Add(_secondPresenter);
    }
}
