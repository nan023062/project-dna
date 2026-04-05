using Dna.ExternalAgent.Models;

namespace Dna.ExternalAgent.Adapters;

internal sealed class CursorExternalAgentAdapter : ExternalAgentAdapterBase
{
    public override ExternalAgentAdapterDescriptor Descriptor { get; } = new()
    {
        ProductId = ExternalAgentConstants.ProductIds.Cursor,
        DisplayName = "Cursor",
        Description = "为 Cursor 生成共享 MCP 配置、规则文件与 Agent 指令文件。",
        InstallMode = ExternalAgentConstants.InstallModes.ProjectFiles,
        Capabilities =
        [
            ExternalAgentConstants.CapabilityIds.Mcp,
            ExternalAgentConstants.CapabilityIds.Rules,
            ExternalAgentConstants.CapabilityIds.Agents
        ],
        ManagedPaths =
        [
            ExternalAgentConstants.ManagedPaths.CursorMcp,
            ExternalAgentConstants.ManagedPaths.CursorRule,
            ExternalAgentConstants.ManagedPaths.CursorAgent
        ]
    };

    public override ExternalAgentPackageResult BuildPackage(ExternalAgentPackageContext context)
    {
        var ruleContent =
            """
            ---
            description: Agentic OS topology orchestration gate
            globs: ["**/*"]
            alwaysApply: true
            ---

            """ + context.SharedInstructionsMarkdown + Environment.NewLine + Environment.NewLine +
            BuildToolListMarkdown(context);

        var agentContent =
            "# Agentic OS for Cursor\n\n" +
            context.SharedInstructionsMarkdown + "\n\n" +
            BuildToolListMarkdown(context);

        var mcpContent =
            $$"""
            {
              "mcpServers": {
                "{{context.Request.ServerName}}": {
                  "url": "{{context.Request.McpEndpoint}}"
                }
              }
            }
            """;

        return new ExternalAgentPackageResult
        {
            Adapter = Descriptor,
            Policy = context.Policy,
            RequiredTools = context.RequiredTools,
            ManagedFiles =
            [
                new()
                {
                    RelativePath = ExternalAgentConstants.ManagedPaths.CursorMcp,
                    Content = mcpContent,
                    Purpose = "为 Cursor 项目级共享 MCP 服务器配置 Agentic OS。"
                },
                new()
                {
                    RelativePath = ExternalAgentConstants.ManagedPaths.CursorRule,
                    Content = ruleContent,
                    Purpose = "用规则文件强制 Cursor 先走拓扑与模块映射，再开始实现。"
                },
                new()
                {
                    RelativePath = ExternalAgentConstants.ManagedPaths.CursorAgent,
                    Content = agentContent,
                    Purpose = "提供给 Cursor 团队共享的 Agent 工作流说明。"
                }
            ],
            Notes =
            [
                ExternalAgentConstants.Notes.TopologyStrictMode
            ]
        };
    }
}
