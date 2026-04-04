using Dna.Workbench.Contracts;
using Dna.Workbench.Knowledge;
using Dna.Workbench.Runtime;
using Dna.Workbench.Tooling;
using Microsoft.Extensions.DependencyInjection;

namespace Dna.Workbench.DependencyInjection;

public static class WorkbenchServiceCollectionExtensions
{
    public static IServiceCollection AddWorkbench(this IServiceCollection services)
    {
        services.AddSingleton<IKnowledgeWorkbenchService, KnowledgeWorkbenchService>();
        services.AddSingleton<IWorkbenchToolService, WorkbenchToolService>();
        services.AddSingleton<IAgentRuntimeEventBus, InMemoryAgentRuntimeEventBus>();
        services.AddSingleton<ITopologyRuntimeProjectionService, TopologyRuntimeProjectionService>();
        services.AddSingleton<IWorkbenchRuntimeService, WorkbenchRuntimeService>();
        services.AddSingleton<IWorkbenchFacade, WorkbenchFacade>();
        return services;
    }
}
