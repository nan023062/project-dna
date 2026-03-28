namespace Dna.Knowledge;

/// <summary>
/// 拓扑排序执行计划 — 对给定模块子集进行排序，被依赖方优先。
/// </summary>
public class ExecutionPlan
{
    public List<string> OrderedModules { get; init; } = [];
    public bool HasCycle { get; init; }
    public string? CycleDescription { get; init; }
}
