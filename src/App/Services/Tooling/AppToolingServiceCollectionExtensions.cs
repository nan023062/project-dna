using Microsoft.Extensions.DependencyInjection;

namespace Dna.App.Services.Tooling;

public static class AppToolingServiceCollectionExtensions
{
    public static IServiceCollection AddAppToolingServices(this IServiceCollection services)
    {
        services.AddSingleton<AppToolingTargetCatalog>();
        services.AddSingleton<AppToolingContentBuilder>();
        services.AddSingleton<AppToolingFileManager>();
        services.AddSingleton<AppIdeToolingService>();
        return services;
    }
}
