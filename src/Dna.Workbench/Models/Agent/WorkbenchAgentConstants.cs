namespace Dna.Workbench.Models.Agent;

public static class WorkbenchAgentConstants
{
    public static class SessionStatus
    {
        public const string Pending = "Pending";
        public const string Running = "Running";
        public const string Completed = "Completed";
        public const string Failed = "Failed";
        public const string Cancelled = "Cancelled";
    }

    public static class EventTypes
    {
        public const string TaskStarted = "TaskStarted";
        public const string TaskCompleted = "TaskCompleted";
        public const string TaskFailed = "TaskFailed";
        public const string NodeEntered = "NodeEntered";
        public const string NodeExited = "NodeExited";
        public const string KnowledgeQueried = "KnowledgeQueried";
        public const string KnowledgeUpdated = "KnowledgeUpdated";
        public const string MemoryRead = "MemoryRead";
        public const string MemoryWritten = "MemoryWritten";
        public const string ToolInvoked = "ToolInvoked";
        public const string RelationTraversed = "RelationTraversed";
        public const string CollaborationTriggered = "CollaborationTriggered";
    }
}
