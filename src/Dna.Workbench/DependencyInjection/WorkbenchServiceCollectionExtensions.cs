using Dna.Workbench.Agent;
using Dna.Workbench.Agent.Pipeline;
using Dna.Workbench.Contracts;
using Dna.Workbench.Knowledge;
using Dna.Workbench.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace Dna.Workbench.DependencyInjection;

public static class WorkbenchServiceCollectionExtensions
{
    public static IServiceCollection AddWorkbench(this IServiceCollection services)
    {
        services.AddSingleton<IKnowledgeWorkbenchService, KnowledgeWorkbenchService>();
        services.AddSingleton<IAgentRuntimeEventBus, InMemoryAgentRuntimeEventBus>();
        services.AddSingleton<ITopologyRuntimeProjectionService, TopologyRuntimeProjectionService>();
        services.AddSingleton<AgentPipelineStore>();
        services.AddSingleton<AgentPipelineRunner>();
        services.AddSingleton<IAgentOrchestrationService, AgentOrchestrationService>();
        services.AddSingleton<IWorkbenchFacade, WorkbenchFacade>();
        return services;
    }
}
