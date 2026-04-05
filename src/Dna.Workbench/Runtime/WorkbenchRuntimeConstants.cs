namespace Dna.Workbench.Runtime;

public static class WorkbenchRuntimeConstants
{
    public static class SourceKinds
    {
        public const string Unknown = "Unknown";
        public const string BuiltInAgent = "BuiltInAgent";
        public const string ExternalAgent = "ExternalAgent";
        public const string Cli = "Cli";
        public const string DesktopUi = "DesktopUi";
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

    public static class ProjectionState
    {
        public const string Idle = "Idle";
        public const string Active = "Active";
        public const string Completed = "Completed";
        public const string Failed = "Failed";
    }

    public static string MapNodeState(string eventType)
    {
        return eventType switch
        {
            EventTypes.TaskCompleted => ProjectionState.Completed,
            EventTypes.TaskFailed => ProjectionState.Failed,
            _ => ProjectionState.Active
        };
    }
}
