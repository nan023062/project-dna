namespace Dna.Memory.Models;

/// <summary>
/// 记忆阶段与鲜活度分离，避免继续用 Freshness 混装“短期/长期”语义。
/// </summary>
public enum MemoryStage
{
    ShortTerm,
    LongTerm
}
