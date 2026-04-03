using Dna.Knowledge.FileProtocol;
using Dna.Knowledge.TopoGraph;
using Dna.Knowledge.TopoGraph.Contracts;
using Dna.Knowledge.TopoGraph.Internal.Builders;
using Dna.Knowledge.Workspace;
using Dna.Memory.Store;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dna.Knowledge;

/// <summary>
/// 知识底座 DI 注册，组装四层模块并共享底层存储实例。
/// </summary>
public static class KnowledgeGraphExtensions
{
    public static IServiceCollection AddKnowledgeGraph(this IServiceCollection services)
    {
        services.AddSingleton(sp => new DnaServiceHolder(sp));

        services.AddSingleton<WorkspaceEngine>(sp => sp.GetRequiredService<DnaServiceHolder>().Workspace);
        services.AddSingleton<IWorkspaceEngine>(sp => sp.GetRequiredService<WorkspaceEngine>());

        services.AddSingleton<MemoryStore>(sp => sp.GetRequiredService<DnaServiceHolder>().Store);
        services.AddSingleton<TopoGraphStore>(sp => sp.GetRequiredService<DnaServiceHolder>().TopoGraphStore);
        services.AddSingleton<ITopoGraphStore>(sp => sp.GetRequiredService<TopoGraphStore>());
        services.AddSingleton<ITopoGraphContextProvider>(sp => new MemoryTopoGraphContextProvider(sp.GetRequiredService<MemoryStore>()));

        services.AddSingleton<GraphEngine>(sp =>
        {
            var store = sp.GetRequiredService<ITopoGraphStore>();
            var contextProvider = sp.GetRequiredService<ITopoGraphContextProvider>();
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            var engine = new GraphEngine(store, contextProvider, loggerFactory.CreateLogger<GraphEngine>());
            var adapter = sp.GetService<IProjectAdapter>();
            if (adapter != null)
                engine.SetAdapter(adapter);
            return engine;
        });
        services.AddSingleton<IGraphEngine>(sp => sp.GetRequiredService<GraphEngine>());

        services.AddSingleton<IMemoryEngine>(sp =>
        {
            var store = sp.GetRequiredService<MemoryStore>();
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            return new MemoryEngine(store, loggerFactory.CreateLogger<MemoryEngine>());
        });

        services.AddSingleton<IGovernanceEngine>(sp =>
        {
            var memoryStore = sp.GetRequiredService<MemoryStore>();
            var topoGraphStore = sp.GetRequiredService<ITopoGraphStore>();
            var graph = sp.GetRequiredService<IGraphEngine>();
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            return new GovernanceEngine(memoryStore, topoGraphStore, graph, loggerFactory);
        });

        // 新系统：文件协议 → TopoGraphFacade
        services.AddSingleton<FileBasedDefinitionStore>();
        services.AddSingleton<ITopoGraphDefinitionStore>(sp => sp.GetRequiredService<FileBasedDefinitionStore>());
        services.AddSingleton<TopologyModelBuilder>();
        services.AddSingleton<ITopoGraphFacade>(sp =>
        {
            var definitionStore = sp.GetRequiredService<ITopoGraphDefinitionStore>();
            var builder = sp.GetRequiredService<TopologyModelBuilder>();
            return new TopoGraphFacade(definitionStore, builder);
        });

        return services;
    }
}
