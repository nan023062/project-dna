using Dna.ExternalAgent.Models;

namespace Dna.ExternalAgent.Adapters;

internal sealed class CodexExternalAgentAdapter : ExternalAgentAdapterBase
{
    public override ExternalAgentAdapterDescriptor Descriptor { get; } = new()
    {
        ProductId = ExternalAgentConstants.ProductIds.Codex,
        DisplayName = "Codex",
        Description = "为 Codex 生成项目级 MCP 配置、提示文件与 Agent 约束文件。",
        InstallMode = ExternalAgentConstants.InstallModes.ProjectConfig,
        Capabilities =
        [
            ExternalAgentConstants.CapabilityIds.Mcp,
            ExternalAgentConstants.CapabilityIds.Prompts,
            ExternalAgentConstants.CapabilityIds.Agents
        ],
        ManagedPaths =
        [
            ExternalAgentConstants.ManagedPaths.CodexConfig,
            ExternalAgentConstants.ManagedPaths.CodexPrompt,
            ExternalAgentConstants.ManagedPaths.CodexAgent
        ]
    };

    public override ExternalAgentPackageResult BuildPackage(ExternalAgentPackageContext context)
    {
        var configContent =
            $$"""
            [mcp_servers.{{context.Request.ServerName}}]
            url = "{{context.Request.McpEndpoint}}"
            """
            .Trim();

        var promptContent =
            "# Agentic OS Codex Topology Workflow\n\n" +
            context.SharedInstructionsMarkdown + "\n\n" +
            BuildToolListMarkdown(context);

        var agentContent =
            "# Agentic OS Codex Agent Contract\n\n" +
            context.SharedInstructionsMarkdown + "\n\n" +
            "## Expected Working Style\n\n" +
            "- 在任何改动前，先用拓扑与模块知识确认边界。\n" +
            "- 禁止在未解析父子层级与依赖关系前直接跨模块编排任务。\n";

        return new ExternalAgentPackageResult
        {
            Adapter = Descriptor,
            Policy = context.Policy,
            RequiredTools = context.RequiredTools,
            ManagedFiles =
            [
                new()
                {
                    RelativePath = ExternalAgentConstants.ManagedPaths.CodexConfig,
                    Content = configContent,
                    Purpose = "为 Codex CLI 与 IDE 扩展共享项目级 MCP 配置。"
                },
                new()
                {
                    RelativePath = ExternalAgentConstants.ManagedPaths.CodexPrompt,
                    Content = promptContent,
                    Purpose = "为 Codex 提供项目级默认工作流提示。"
                },
                new()
                {
                    RelativePath = ExternalAgentConstants.ManagedPaths.CodexAgent,
                    Content = agentContent,
                    Purpose = "为 Codex 代理任务提供 Agentic OS 的拓扑治理约束。"
                }
            ],
            Notes =
            [
                ExternalAgentConstants.Notes.TopologyStrictMode
            ]
        };
    }
}
