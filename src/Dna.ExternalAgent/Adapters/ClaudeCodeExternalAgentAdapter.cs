using System.Text.Json;
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
        var manifestContent = JsonSerializer.Serialize(new
        {
            schemaVersion = ExternalAgentConstants.ClaudePlugin.ManifestSchemaVersion,
            id = ExternalAgentConstants.ClaudePlugin.Id,
            name = "Agentic OS Topology Guard",
            version = ExternalAgentConstants.ClaudePlugin.ManifestVersion,
            description = "Enhanced preview bundle for Claude Code plugin installation.",
            author = "Agentic OS",
            installMode = "preview-bundle",
            capabilities = Descriptor.Capabilities,
            mcp = new
            {
                serverName = context.Request.ServerName,
                endpoint = context.Request.McpEndpoint,
                requiredTools = context.RequiredTools.Select(tool => tool.Name).ToArray()
            },
            entrypoints = new
            {
                commands = new[]
                {
                    new
                    {
                        name = ExternalAgentConstants.ClaudePlugin.CommandName,
                        file = "commands/agentic-os-topology.md",
                        description = "Run the Agentic OS topology-first workflow before implementation."
                    }
                },
                hooks = new[]
                {
                    new
                    {
                        phase = "task-start",
                        file = "hooks/agentic-os-topology.md",
                        description = "Validate topology, module scope, and governance boundary before work starts."
                    },
                    new
                    {
                        phase = "task-end",
                        file = "hooks/agentic-os-topology.md",
                        description = "Require task summary, decisions, and lessons before the task closes."
                    }
                }
            },
            policy = new
            {
                strictTopologyMode = context.Policy.StrictMode,
                workflowRules = context.Policy.WorkflowRules
            }
        }, new JsonSerializerOptions { WriteIndented = true });

        var readmeContent =
            "# Agentic OS Claude Code Plugin Preview\n\n" +
            "这个 bundle 会把 Claude Code 收口到 Agentic OS 的拓扑优先工作流。\n\n" +
            "## Bundle Layout\n\n" +
            $"- `{ExternalAgentConstants.ManagedPaths.ClaudePluginManifest}`: 预览 manifest，声明 MCP、命令与 hook 入口。\n" +
            $"- `{ExternalAgentConstants.ManagedPaths.ClaudePluginCommand}`: 斜杠命令入口，要求先解析拓扑与模块。\n" +
            $"- `{ExternalAgentConstants.ManagedPaths.ClaudePluginHook}`: 任务开始/结束检查清单。\n" +
            $"- MCP server: `{context.Request.ServerName}` -> `{context.Request.McpEndpoint}`\n\n" +
            context.SharedInstructionsMarkdown + "\n\n" +
            BuildToolListMarkdown(context);

        var commandContent =
            $"# {ExternalAgentConstants.ClaudePlugin.CommandName}\n\n" +
            "执行任何任务前，必须先完成下面顺序：\n\n" +
            "1. 调用 `knowledge.get_topology`\n" +
            "2. 调用 `knowledge.get_workspace_snapshot`\n" +
            "3. 解析目标模块、父子层级、依赖方向与协作边界\n" +
            "4. 如需执行任务，先走 `tasks.resolve_requirement_support` / `tasks.start_task`\n" +
            "5. 结束时调用 `tasks.end_task` 与 `memory.remember`\n\n" +
            context.SharedInstructionsMarkdown;

        var hookContent =
            "# Agentic OS Hook Policy\n\n" +
            "## On Task Start\n\n" +
            "- 必须先拉取拓扑、workspace snapshot 与模块知识。\n" +
            "- 必须确认当前任务只落在单一模块范围内。\n" +
            "- 如果跨模块，必须显式说明 Collaboration / Team 边界，并拆分成多个 task。\n\n" +
            "## On Task End\n\n" +
            "- 必须回写任务 summary、关键决策和 lessons。\n" +
            "- 如果被阻塞，必须记录 pending dependencies 或失败原因。\n";

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
