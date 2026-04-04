using Dna.Workbench.Governance;

namespace Dna.Workbench.Contracts;

public interface IWorkbenchGovernanceService
{
    /// <summary>
    /// 解析治理范围并返回确定性的治理上下文。
    /// Workbench 不负责判断“应该怎么治理”，只负责把范围、候选模块、规则和诊断结果提供给 Agent。
    /// </summary>
    Task<WorkbenchGovernanceContext> ResolveGovernanceAsync(
        WorkbenchGovernanceRequest request,
        CancellationToken cancellationToken = default);
}
