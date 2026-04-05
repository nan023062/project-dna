using Dna.Workbench.Tasks;

namespace Dna.Workbench.Contracts;

public interface IWorkbenchTaskService
{
    /// <summary>
    /// 提供确定性的需求收口辅助。
    /// 输入只是原始需求文本或提示。
    /// 这里只做基于拓扑与模块知识的候选模块检索，不做任何大模型推理、语义规划或任务编排。
    /// </summary>
    Task<IReadOnlyList<WorkbenchTaskCandidate>> ResolveRequirementSupportAsync(
        WorkbenchRequirementRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 启动单模块任务并返回受限上下文。
    /// Workbench 只负责校验、加锁和上下文供给，不负责决定整体任务链。
    /// </summary>
    Task<WorkbenchTaskStartResponse> StartTaskAsync(
        WorkbenchTaskRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 结束单模块任务并回写结果，同时返回完成回执。
    /// </summary>
    Task<WorkbenchTaskEndResponse> EndTaskAsync(
        WorkbenchTaskResult result,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 返回当前所有活动任务，供上层做任务链协调、恢复和 UI 展示。
    /// </summary>
    Task<IReadOnlyList<WorkbenchActiveTaskSnapshot>> ListActiveTasksAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 返回最近完成的任务摘要，供上层判断前置依赖是否满足。
    /// </summary>
    Task<IReadOnlyList<WorkbenchCompletedTaskSnapshot>> ListCompletedTasksAsync(
        int limit = 50,
        CancellationToken cancellationToken = default);
}
