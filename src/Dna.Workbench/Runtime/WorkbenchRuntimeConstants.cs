using Dna.Workbench.Models.Agent;

namespace Dna.Workbench.Runtime;

internal static class WorkbenchRuntimeConstants
{
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
            WorkbenchAgentConstants.EventTypes.TaskCompleted => ProjectionState.Completed,
            WorkbenchAgentConstants.EventTypes.TaskFailed => ProjectionState.Failed,
            _ => ProjectionState.Active
        };
    }
}
