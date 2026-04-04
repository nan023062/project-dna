using Dna.ExternalAgent.Models;

namespace Dna.ExternalAgent.Adapters;

internal sealed class CopilotExternalAgentAdapter : ExternalAgentAdapterBase
{
    public override ExternalAgentAdapterDescriptor Descriptor { get; } = new()
    {
        ProductId = ExternalAgentConstants.ProductIds.Copilot,
        DisplayName = "GitHub Copilot",
        Description = "为 GitHub Copilot 生成仓库级 custom instructions、path instructions 与 agent instructions。",
        InstallMode = ExternalAgentConstants.InstallModes.ProjectFiles,
        Capabilities =
        [
            ExternalAgentConstants.CapabilityIds.CustomInstructions,
            ExternalAgentConstants.CapabilityIds.Agents
        ],
        ManagedPaths =
        [
            ExternalAgentConstants.ManagedPaths.CopilotInstructions,
            ExternalAgentConstants.ManagedPaths.CopilotPathInstructions,
            ExternalAgentConstants.ManagedPaths.CopilotAgents
        ]
    };

    public override ExternalAgentPackageResult BuildPackage(ExternalAgentPackageContext context)
    {
        var repositoryInstructions =
            "# Agentic OS Copilot Instructions\n\n" +
            context.SharedInstructionsMarkdown + "\n\n" +
            BuildToolListMarkdown(context);

        var pathInstructions =
            "# Agentic OS Topology Path Rule\n\n" +
            "当 Copilot 处理任意源码路径时，先确认该路径属于哪个模块，再按模块边界执行分析与修改。\n";

        var agentInstructions =
            "# Agentic OS Agent Instructions\n\n" +
            "所有 Agent 任务都必须以 Agentic OS 的拓扑、映射和关系类为唯一编排依据。\n\n" +
            context.SharedInstructionsMarkdown;

        return new ExternalAgentPackageResult
        {
            Adapter = Descriptor,
            Policy = context.Policy,
            RequiredTools = context.RequiredTools,
            ManagedFiles =
            [
                new()
                {
                    RelativePath = ExternalAgentConstants.ManagedPaths.CopilotInstructions,
                    Content = repositoryInstructions,
                    Purpose = "为 GitHub Copilot 提供仓库级默认指令。"
                },
                new()
                {
                    RelativePath = ExternalAgentConstants.ManagedPaths.CopilotPathInstructions,
                    Content = pathInstructions,
                    Purpose = "为 GitHub Copilot 提供路径级拓扑约束。"
                },
                new()
                {
                    RelativePath = ExternalAgentConstants.ManagedPaths.CopilotAgents,
                    Content = agentInstructions,
                    Purpose = "为 Copilot 的 agent 模式提供全局 Agentic OS 指令。"
                }
            ],
            Notes =
            [
                ExternalAgentConstants.Notes.TopologyStrictMode
            ]
        };
    }
}
