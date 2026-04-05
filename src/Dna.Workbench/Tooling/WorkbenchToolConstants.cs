namespace Dna.Workbench.Tooling;

public static class WorkbenchToolConstants
{
    public static class Groups
    {
        public const string Knowledge = "Knowledge";
        public const string Memory = "Memory";
        public const string Runtime = "Runtime";
        public const string Tasks = "Tasks";
        public const string Governance = "Governance";
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
        
        public const string ResolveRequirementSupport = "tasks.resolve_requirement_support";
        public const string StartTask = "tasks.start_task";
        public const string EndTask = "tasks.end_task";
        public const string ListActiveTasks = "tasks.list_active_tasks";
        public const string ListCompletedTasks = "tasks.list_completed_tasks";
        
        public const string ResolveGovernance = "governance.resolve_governance";
    }
}