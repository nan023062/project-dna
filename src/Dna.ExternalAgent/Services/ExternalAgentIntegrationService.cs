using System.Text;
using Dna.ExternalAgent.Contracts;
using Dna.ExternalAgent.Models;

namespace Dna.ExternalAgent.Services;

internal sealed class ExternalAgentIntegrationService(
    IExternalAgentAdapterCatalog adapterCatalog,
    IExternalAgentToolCatalogService toolCatalog) : IExternalAgentIntegrationService
{
    public IReadOnlyList<ExternalAgentAdapterDescriptor> ListAdapters()
        => adapterCatalog.ListAdapters();

    public ExternalAgentPackageResult BuildPackage(ExternalAgentPackageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var adapter = adapterCatalog.FindAdapter(request.ProductId)
            ?? throw new InvalidOperationException($"Unsupported external agent product: {request.ProductId}");

        var requiredToolNames = CreateRequiredToolNames();
        var requiredTools = toolCatalog.ListTools()
            .Where(tool => requiredToolNames.Contains(tool.Name, StringComparer.OrdinalIgnoreCase))
            .ToList();

        var policy = new ExternalAgentTopologyPolicy
        {
            StrictMode = request.StrictTopologyMode,
            RequiredToolNames = requiredToolNames,
            WorkflowRules =
            [
                ExternalAgentConstants.DefaultWorkflowRules.TopologyFirst,
                ExternalAgentConstants.DefaultWorkflowRules.ResolveModule,
                ExternalAgentConstants.DefaultWorkflowRules.RespectContainment,
                ExternalAgentConstants.DefaultWorkflowRules.RespectDependencies,
                ExternalAgentConstants.DefaultWorkflowRules.RespectCollaborations,
                ExternalAgentConstants.DefaultWorkflowRules.WriteBackMemory
            ]
        };

        var context = new ExternalAgentPackageContext
        {
            Request = request,
            Policy = policy,
            RequiredTools = requiredTools,
            SharedInstructionsMarkdown = BuildSharedInstructions(request, policy)
        };

        return adapter.BuildPackage(context);
    }

    private static IReadOnlyList<string> CreateRequiredToolNames()
    {
        return
        [
            ExternalAgentConstants.DefaultToolNames.GetTopology,
            ExternalAgentConstants.DefaultToolNames.GetWorkspaceSnapshot,
            ExternalAgentConstants.DefaultToolNames.GetModuleKnowledge,
            ExternalAgentConstants.DefaultToolNames.SaveModuleKnowledge,
            ExternalAgentConstants.DefaultToolNames.Remember,
            ExternalAgentConstants.DefaultToolNames.Recall,
            ExternalAgentConstants.DefaultToolNames.GetRuntimeProjection,
            ExternalAgentConstants.DefaultToolNames.ResolveRequirementSupport,
            ExternalAgentConstants.DefaultToolNames.StartTask,
            ExternalAgentConstants.DefaultToolNames.EndTask,
            ExternalAgentConstants.DefaultToolNames.ListActiveTasks,
            ExternalAgentConstants.DefaultToolNames.ListCompletedTasks,
            ExternalAgentConstants.DefaultToolNames.ResolveGovernance
        ];
    }

    private static string BuildSharedInstructions(
        ExternalAgentPackageRequest request,
        ExternalAgentTopologyPolicy policy)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Agentic OS External Agent Workflow");
        builder.AppendLine();
        builder.AppendLine($"- Product: `{request.ProductId}`");
        builder.AppendLine($"- MCP endpoint: `{request.McpEndpoint}`");
        builder.AppendLine($"- Strict topology mode: `{policy.StrictMode}`");
        builder.AppendLine();
        builder.AppendLine("## Mandatory Workflow");
        builder.AppendLine();

        foreach (var rule in policy.WorkflowRules)
            builder.AppendLine($"- {rule}");

        builder.AppendLine();
        builder.AppendLine("## Required Topology Semantics");
        builder.AppendLine();
        builder.AppendLine("- `Project` / `Department` 负责父级分组与层级导航，不应直接跳过。");
        builder.AppendLine("- `Technical` 负责单一技术能力，任务编排必须遵守其单向依赖关系。");
        builder.AppendLine("- `Team` 负责跨模块协作与业务交付，跨模块工作必须通过 Team / Collaboration 关系显式确认。");
        builder.AppendLine();
        builder.AppendLine("## Non-Negotiable Rules");
        builder.AppendLine();
        builder.AppendLine("- 不允许只按文件路径猜测模块归属，必须先解析模块映射。");
        builder.AppendLine("- 不允许在未确认父子层级时直接修改深层子模块。");
        builder.AppendLine("- 不允许逆向依赖式编排任务。");
        builder.AppendLine("- 不允许跳过记忆回写。");
        return builder.ToString().TrimEnd();
    }
}
