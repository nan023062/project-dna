using Dna.Core.Config;
using Dna.Knowledge;
using Dna.Memory.Models;
using Dna.Memory.Services;
using Microsoft.Extensions.Logging;

namespace Dna.Memory.Store;

/// <summary>
/// MemoryStore 对外 API，只负责记忆读写/查询/召回及其内部组件装配。
/// </summary>
public partial class MemoryStore
{
    private MemoryWriter? _writer;
    private MemoryReader? _reader;
    private MemoryRecallEngine? _recallEngine;
    private ITopoGraphStore? _topoGraphStore;

    public void BuildInternals(
        IHttpClientFactory httpClientFactory,
        ProjectConfig config,
        ILoggerFactory loggerFactory,
        ITopoGraphStore topoGraphStore)
    {
        _topoGraphStore = topoGraphStore;

        var vectorIndex = new VectorIndex();
        var embeddingService = new EmbeddingService(
            httpClientFactory,
            config,
            loggerFactory.CreateLogger<EmbeddingService>());

        _writer = new MemoryWriter(
            this,
            topoGraphStore,
            embeddingService,
            vectorIndex,
            loggerFactory.CreateLogger<MemoryWriter>());
        _reader = new MemoryReader(this, loggerFactory.CreateLogger<MemoryReader>());
        _recallEngine = new MemoryRecallEngine(
            this,
            vectorIndex,
            embeddingService,
            loggerFactory.CreateLogger<MemoryRecallEngine>());
    }

    /// <summary>写入一条记忆（自动生成 embedding + 索引同步）</summary>
    public Task<MemoryEntry> RememberAsync(RememberRequest request)
        => Writer.RememberAsync(request);

    /// <summary>更新一条记忆</summary>
    public Task<MemoryEntry> UpdateMemoryAsync(string memoryId, RememberRequest request)
        => Writer.UpdateAsync(memoryId, request);

    /// <summary>标记一条记忆被新知识取代</summary>
    public void Supersede(string oldMemoryId, string newMemoryId)
        => Writer.Supersede(oldMemoryId, newMemoryId);

    /// <summary>批量写入</summary>
    public Task<List<MemoryEntry>> RememberBatchAsync(List<RememberRequest> requests)
        => Writer.RememberBatchAsync(requests);

    /// <summary>验证一条记忆仍有效，重置鲜活度为 Fresh</summary>
    public void VerifyMemory(string memoryId)
        => Writer.Verify(memoryId);

    /// <summary>获取业务系统的全职能知识汇总</summary>
    public FeatureKnowledgeSummary GetFeatureSummary(string featureId)
        => Reader.GetFeatureSummary(featureId);

    /// <summary>获取职能知识汇总（按层级分组）</summary>
    public DisciplineKnowledgeSummary GetDisciplineSummary(string disciplineId)
        => Reader.GetDisciplineSummary(disciplineId);

    /// <summary>语义召回 — 四通道检索 + 融合排序 + 约束链展开</summary>
    public Task<RecallResult> RecallAsync(RecallQuery query)
        => RecallEngine.RecallAsync(query);

    private MemoryWriter Writer => _writer ?? throw new InvalidOperationException("MemoryStore not initialized. Call BuildInternals first.");
    private MemoryReader Reader => _reader ?? throw new InvalidOperationException("MemoryStore not initialized. Call BuildInternals first.");
    private MemoryRecallEngine RecallEngine => _recallEngine ?? throw new InvalidOperationException("MemoryStore not initialized. Call BuildInternals first.");
    internal MemoryWriter WriterService => Writer;
    internal MemoryReader ReaderService => Reader;
    internal MemoryRecallEngine RecallService => RecallEngine;
    internal ITopoGraphStore TopoGraphStore => _topoGraphStore ?? throw new InvalidOperationException("MemoryStore not initialized. Call BuildInternals first.");
}
