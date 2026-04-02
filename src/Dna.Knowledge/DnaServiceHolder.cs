using Dna.Core.Config;
using Dna.Knowledge.Workspace;
using Dna.Memory.Store;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dna.Knowledge;

/// <summary>
/// 引擎间共享的基础设施：MemoryStore + WorkspaceEngine。
/// 由 DI 注册为 Singleton，三个引擎通过此对象共享底层存储。
/// </summary>
internal sealed class DnaServiceHolder : IDisposable
{
    public MemoryStore Store { get; }
    public TopoGraphStore TopoGraphStore { get; }
    public WorkspaceEngine Workspace { get; }

    public DnaServiceHolder(IServiceProvider sp)
    {
        var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
        var config = sp.GetRequiredService<ProjectConfig>();
        var httpFactory = sp.GetRequiredService<IHttpClientFactory>();

        TopoGraphStore = new TopoGraphStore(sp);
        Store = new MemoryStore(sp);
        Store.BuildInternals(httpFactory, config, loggerFactory, TopoGraphStore);
        var treeCache = new WorkspaceTreeCache(loggerFactory.CreateLogger<WorkspaceTreeCache>());
        Workspace = new WorkspaceEngine(treeCache);
    }

    public void Dispose()
    {
        Workspace.Dispose();
        Store.Dispose();
        GC.SuppressFinalize(this);
    }
}
