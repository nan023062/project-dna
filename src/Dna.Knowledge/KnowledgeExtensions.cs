using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dna.Knowledge;

/// <summary>
/// 知识图谱 DI 注册 — 三引擎架构，共享底层 MemoryStore。
/// </summary>
public static class KnowledgeGraphExtensions
{
    public static IServiceCollection AddKnowledgeGraph(this IServiceCollection services)
    {
        services.AddSingleton(sp => new DnaServiceHolder(sp));

        services.AddSingleton<GraphEngine>(sp =>
        {
            var holder = sp.GetRequiredService<DnaServiceHolder>();
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            var engine = new GraphEngine(holder, loggerFactory.CreateLogger<GraphEngine>());
            var adapter = sp.GetService<IProjectAdapter>();
            if (adapter != null)
                engine.SetAdapter(adapter);
            return engine;
        });
        services.AddSingleton<IGraphEngine>(sp => sp.GetRequiredService<GraphEngine>());

        services.AddSingleton<IMemoryEngine>(sp =>
        {
            var holder = sp.GetRequiredService<DnaServiceHolder>();
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            return new MemoryEngine(holder, loggerFactory.CreateLogger<MemoryEngine>());
        });

        services.AddSingleton<IGovernanceEngine>(sp =>
        {
            var holder = sp.GetRequiredService<DnaServiceHolder>();
            var graph = sp.GetRequiredService<GraphEngine>();
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            return new GovernanceEngine(holder, graph, loggerFactory);
        });

        return services;
    }
}
