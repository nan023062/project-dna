namespace Dna.ExternalAgent.Models;

public static class ExternalAgentConstants
{
    public static class ProductIds
    {
        public const string Cursor = "cursor";
        public const string ClaudeCode = "claude-code";
        public const string Codex = "codex";
        public const string Copilot = "copilot";
    }

    public static class CapabilityIds
    {
        public const string Mcp = "mcp";
        public const string Rules = "rules";
        public const string Prompts = "prompts";
        public const string CustomInstructions = "custom-instructions";
        public const string Agents = "agents";
        public const string Plugins = "plugins";
        public const string Hooks = "hooks";
        public const string SlashCommands = "slash-commands";
    }

    public static class InstallModes
    {
        public const string ProjectFiles = "project-files";
        public const string ProjectConfig = "project-config";
        public const string PluginBundle = "plugin-bundle";
    }

    public static class ManagedPaths
    {
        public const string CursorMcp = ".cursor/mcp.json";
        public const string CursorRule = ".cursor/rules/agentic-os-topology.mdc";
        public const string CursorAgent = ".cursor/agents/agentic-os-topology.md";

        public const string CodexConfig = ".codex/config.toml";
        public const string CodexPrompt = ".codex/prompts/agentic-os-topology.md";
        public const string CodexAgent = ".codex/agents/agentic-os-topology.md";

        public const string ClaudePluginManifest = ".claude-plugin/plugin.json";
        public const string ClaudePluginReadme = ".claude-plugin/README.md";
        public const string ClaudePluginCommand = ".claude-plugin/commands/agentic-os-topology.md";
        public const string ClaudePluginHook = ".claude-plugin/hooks/agentic-os-topology.md";

        public const string CopilotInstructions = ".github/copilot-instructions.md";
        public const string CopilotPathInstructions = ".github/instructions/agentic-os-topology.instructions.md";
        public const string CopilotAgents = "AGENTS.md";
    }

    public static class DefaultToolNames
    {
        public const string GetTopology = "knowledge.get_topology";
        public const string GetWorkspaceSnapshot = "knowledge.get_workspace_snapshot";
        public const string GetModuleKnowledge = "knowledge.get_module_knowledge";
        public const string SaveModuleKnowledge = "knowledge.save_module_knowledge";
        public const string Remember = "memory.remember";
        public const string Recall = "memory.recall";
        public const string GetRuntimeProjection = "runtime.get_projection";
        public const string ResolveRequirementSupport = "tasks.resolve_requirement_support";
        public const string StartTask = "tasks.start_task";
        public const string EndTask = "tasks.end_task";
        public const string ListActiveTasks = "tasks.list_active_tasks";
        public const string ListCompletedTasks = "tasks.list_completed_tasks";
        public const string ResolveGovernance = "governance.resolve_governance";
    }

    public static class DefaultWorkflowRules
    {
        public const string TopologyFirst = "先解析知识拓扑，再开始改动。";
        public const string ResolveModule = "任何任务都必须先解析目标模块与父子层级。";
        public const string RespectContainment = "进入子模块前必须确认父模块上下文；跨层跳转必须显式说明原因。";
        public const string RespectDependencies = "只能沿已声明或已计算出的依赖方向工作，禁止逆向依赖式编排。";
        public const string RespectCollaborations = "涉及协作关系时，必须显式检查 Collaboration / Team 边界。";
        public const string WriteBackMemory = "关键决策、任务完成与经验教训必须回写记忆。";
    }

    public static class Notes
    {
        public const string ClaudePluginBundlePreview = "当前 Claude Code 适配器先输出 plugin bundle 预览产物；正式安装器与 manifest 细节后续补齐。";
        public const string TopologyStrictMode = "该包默认启用严格拓扑模式：外置 Agent 必须先走拓扑解析与模块映射，再进入实现阶段。";
    }
}
