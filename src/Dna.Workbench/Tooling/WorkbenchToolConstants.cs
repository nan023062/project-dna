namespace Dna.Workbench.Tooling;

public static class WorkbenchToolConstants
{
    public static class Groups
    {
        public const string Knowledge = "Knowledge";
        public const string Memory = "Memory";
        public const string Runtime = "Runtime";
    }

    public static class ToolNames
    {
        public const string GetTopology = "knowledge.get_topology";
        public const string GetWorkspaceSnapshot = "knowledge.get_workspace_snapshot";
        public const string GetModuleKnowledge = "knowledge.get_module_knowledge";
        public const string SaveModuleKnowledge = "knowledge.save_module_knowledge";
        public const string Remember = "memory.remember";
        public const string Recall = "memory.recall";
        public const string GetRuntimeProjection = "runtime.get_projection";
    }
}
