using Microsoft.Extensions.DependencyInjection;

namespace Dna.Client.Services.Tooling;

public static class ClientToolingServiceCollectionExtensions
{
    public static IServiceCollection AddClientToolingServices(this IServiceCollection services)
    {
        services.AddSingleton<ClientToolingTargetCatalog>();
        services.AddSingleton<ClientToolingContentBuilder>();
        services.AddSingleton<ClientToolingFileManager>();
        services.AddSingleton<ClientIdeToolingService>();
        return services;
    }
}
