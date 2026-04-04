using Dna.Agent.Contracts;
using Dna.Agent.Chat;
using Dna.Agent.Orchestration;
using Dna.Agent.Pipeline;
using Microsoft.Extensions.DependencyInjection;

namespace Dna.Agent.DependencyInjection;

public static class AgentServiceCollectionExtensions
{
    public static IServiceCollection AddAgent(this IServiceCollection services)
    {
        services.AddSingleton<AgentChatSessionStore>();
        services.AddSingleton<AgentPipelineStore>();
        services.AddSingleton<AgentPipelineRunner>();
        services.AddSingleton<IAgentOrchestrationService, AgentOrchestrationService>();
        services.AddSingleton<IAgentChatService, AgentChatService>();
        return services;
    }
}
