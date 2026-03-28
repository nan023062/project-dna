using Dna.Core.Config;
using Dna.Knowledge.Project;
using Dna.Memory.Store;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dna.Knowledge;

/// <summary>
/// 引擎间共享的基础设施：MemoryStore + ProjectTreeCache。
/// 由 DI 注册为 Singleton，三个引擎通过此对象共享底层存储。
/// </summary>
internal sealed class DnaServiceHolder : IDisposable
{
    public MemoryStore Store { get; }
    public ProjectTreeCache TreeCache { get; }

    public DnaServiceHolder(IServiceProvider sp)
    {
        var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
        var config = sp.GetRequiredService<ProjectConfig>();
        var httpFactory = sp.GetRequiredService<IHttpClientFactory>();

        Store = new MemoryStore(sp);
        Store.BuildInternals(httpFactory, config, loggerFactory);
        TreeCache = new ProjectTreeCache(loggerFactory.CreateLogger<ProjectTreeCache>());
    }

    public void Dispose()
    {
        TreeCache.Dispose();
        Store.Dispose();
        GC.SuppressFinalize(this);
    }
}
