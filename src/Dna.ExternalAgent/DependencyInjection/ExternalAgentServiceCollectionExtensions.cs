using Dna.ExternalAgent.Adapters;
using Dna.ExternalAgent.Contracts;
using Dna.ExternalAgent.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Dna.ExternalAgent.DependencyInjection;

public static class ExternalAgentServiceCollectionExtensions
{
    public static IServiceCollection AddExternalAgent(this IServiceCollection services)
    {
        services.AddSingleton<IExternalAgentAdapter, CursorExternalAgentAdapter>();
        services.AddSingleton<IExternalAgentAdapter, ClaudeCodeExternalAgentAdapter>();
        services.AddSingleton<IExternalAgentAdapter, CodexExternalAgentAdapter>();
        services.AddSingleton<IExternalAgentAdapter, CopilotExternalAgentAdapter>();
        services.AddSingleton<IExternalAgentAdapterCatalog, ExternalAgentAdapterCatalog>();
        services.AddSingleton<IExternalAgentToolCatalogService, ExternalAgentToolCatalogService>();
        services.AddSingleton<ExternalAgentFileManager>();
        services.AddSingleton<IExternalAgentIntegrationService, ExternalAgentIntegrationService>();
        services.AddSingleton<IExternalAgentToolingService, ExternalAgentToolingService>();
        return services;
    }
}
