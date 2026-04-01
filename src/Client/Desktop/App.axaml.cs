using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Dna.Client.Desktop;

public partial class App : Application
{
    private readonly EmbeddedClientHost _host = new();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow(_host);
            desktop.Exit += async (_, _) =>
            {
                await _host.StopAsync();
                ClientDesktopLog.Shutdown();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
