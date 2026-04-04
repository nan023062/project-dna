using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Dna.App.Desktop.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Dna.App.Desktop;

public partial class App : Application
{
    private readonly EmbeddedAppHost _host = new();
    public new static App? Current => Application.Current as App;
    public IServiceProvider? Services { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        Services = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow(_host);
            desktop.Exit += async (_, _) =>
            {
                await _host.StopAsync();
                AppDesktopLog.Shutdown();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IDnaApiClient, DnaApiClient>();
    }
}
