using Dna.ExternalAgent.Models;

namespace Dna.ExternalAgent.Adapters;

internal sealed class ClaudeCodeExternalAgentAdapter : ExternalAgentAdapterBase
{
    public override ExternalAgentAdapterDescriptor Descriptor { get; } = new()
    {
        ProductId = ExternalAgentConstants.ProductIds.ClaudeCode,
        DisplayName = "Claude Code",
        Description = "为 Claude Code 生成 plugin bundle 预览内容，统一其 slash command、hooks 与 MCP 工作流。",
        InstallMode = ExternalAgentConstants.InstallModes.PluginBundle,
        Capabilities =
        [
            ExternalAgentConstants.CapabilityIds.Plugins,
            ExternalAgentConstants.CapabilityIds.Mcp,
            ExternalAgentConstants.CapabilityIds.SlashCommands,
            ExternalAgentConstants.CapabilityIds.Hooks
        ],
        ManagedPaths =
        [
            ExternalAgentConstants.ManagedPaths.ClaudePluginManifest,
            ExternalAgentConstants.ManagedPaths.ClaudePluginReadme,
            ExternalAgentConstants.ManagedPaths.ClaudePluginCommand,
            ExternalAgentConstants.ManagedPaths.ClaudePluginHook
        ]
    };

    public override ExternalAgentPackageResult BuildPackage(ExternalAgentPackageContext context)
    {
        var manifestContent =
            $$"""
            {
              "name": "agentic-os-topology",
              "displayName": "Agentic OS Topology Guard",
              "version": "0.1.0",
              "description": "Preview bundle for Claude Code plugin installation."
            }
            """;

        var readmeContent =
            "# Agentic OS Claude Code Plugin Preview\n\n" +
            context.SharedInstructionsMarkdown + "\n\n" +
            BuildToolListMarkdown(context);

        var commandContent =
            "# /agentic-os-topology\n\n" +
            "执行任何任务前，先确认目标模块、父子层级、依赖方向与协作边界，然后再进入实现阶段。\n\n" +
            context.SharedInstructionsMarkdown;

        var hookContent =
            "# Agentic OS Hook Policy\n\n" +
            "- 当任务开始时，必须先拉取拓扑和模块知识。\n" +
            "- 当任务结束时，必须回写记忆与关键决策。\n" +
            "- 当跨模块协作时，必须显式说明 Collaboration / Team 边界。\n";

        return new ExternalAgentPackageResult
        {
            Adapter = Descriptor,
            Policy = context.Policy,
            RequiredTools = context.RequiredTools,
            ManagedFiles =
            [
                new()
                {
                    RelativePath = ExternalAgentConstants.ManagedPaths.ClaudePluginManifest,
                    Content = manifestContent,
                    Purpose = "Claude Code plugin bundle 的预览 manifest。"
                },
                new()
                {
                    RelativePath = ExternalAgentConstants.ManagedPaths.ClaudePluginReadme,
                    Content = readmeContent,
                    Purpose = "说明该 bundle 如何把 Claude Code 收敛到 Agentic OS 的拓扑工作流。"
                },
                new()
                {
                    RelativePath = ExternalAgentConstants.ManagedPaths.ClaudePluginCommand,
                    Content = commandContent,
                    Purpose = "提供 Claude Code 的 slash command 预览内容。"
                },
                new()
                {
                    RelativePath = ExternalAgentConstants.ManagedPaths.ClaudePluginHook,
                    Content = hookContent,
                    Purpose = "提供 Claude Code 的 hook 预览内容。"
                }
            ],
            Notes =
            [
                ExternalAgentConstants.Notes.TopologyStrictMode,
                ExternalAgentConstants.Notes.ClaudePluginBundlePreview
            ]
        };
    }
}
