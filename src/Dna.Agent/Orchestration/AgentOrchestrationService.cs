using Dna.Agent.Contracts;
using Dna.Agent.Models;
using Dna.Workbench.Contracts;
using Dna.Workbench.Runtime;

namespace Dna.Agent.Orchestration;

internal sealed class AgentOrchestrationService(
    IWorkbenchRuntimeService runtimeService) : IAgentOrchestrationService
{
    private readonly object _gate = new();
    private readonly Dictionary<string, AgentSessionSnapshot> _sessions = new(StringComparer.OrdinalIgnoreCase);

    public Task<AgentSessionSnapshot> StartSessionAsync(
        AgentTaskRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var session = new AgentSessionSnapshot
        {
            SessionId = Guid.NewGuid().ToString("N"),
            Status = AgentSessionConstants.SessionStatus.Pending,
            StartedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            Task = CloneRequest(request),
            Timeline = []
        };

        lock (_gate)
        {
            _sessions[session.SessionId] = session;
        }

        Publish(new AgentTimelineEvent
        {
            SessionId = session.SessionId,
            EventType = WorkbenchRuntimeConstants.EventTypes.TaskStarted,
            Message = string.IsNullOrWhiteSpace(request.Title) ? "Agent session started." : request.Title,
            Metadata = CloneMetadata(request.Metadata)
        });

        foreach (var nodeId in request.TargetNodeIds.Where(static nodeId => !string.IsNullOrWhiteSpace(nodeId)))
        {
            Publish(new AgentTimelineEvent
            {
                SessionId = session.SessionId,
                EventType = WorkbenchRuntimeConstants.EventTypes.NodeEntered,
                NodeId = nodeId,
                Message = $"Entered node {nodeId}.",
                Metadata = CloneMetadata(request.Metadata)
            });
        }

        return Task.FromResult(GetRequiredSession(session.SessionId));
    }

    public AgentSessionSnapshot? GetSession(string sessionId)
    {
        lock (_gate)
        {
            return _sessions.TryGetValue(sessionId, out var session) ? Clone(session) : null;
        }
    }

    public IReadOnlyList<AgentSessionSnapshot> ListSessions()
    {
        lock (_gate)
        {
            return _sessions.Values
                .OrderByDescending(session => session.UpdatedAtUtc)
                .Select(Clone)
                .ToList();
        }
    }

    public Task<bool> CancelSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (!_sessions.TryGetValue(sessionId, out var session))
                return Task.FromResult(false);

            _sessions[sessionId] = new AgentSessionSnapshot
            {
                SessionId = session.SessionId,
                Status = AgentSessionConstants.SessionStatus.Cancelled,
                StartedAtUtc = session.StartedAtUtc,
                UpdatedAtUtc = DateTime.UtcNow,
                Task = session.Task,
                Timeline = session.Timeline
                    .Concat(
                    [
                        new AgentTimelineEvent
                        {
                            SessionId = sessionId,
                            EventType = WorkbenchRuntimeConstants.EventTypes.TaskFailed,
                            Message = "Agent session cancelled."
                        }
                    ])
                    .ToList()
            };
        }

        runtimeService.ResetProjection(sessionId);
        return Task.FromResult(true);
    }

    private void Publish(AgentTimelineEvent agentEvent)
    {
        lock (_gate)
        {
            if (_sessions.TryGetValue(agentEvent.SessionId, out var session))
            {
                _sessions[agentEvent.SessionId] = new AgentSessionSnapshot
                {
                    SessionId = session.SessionId,
                    Status = ResolveStatus(session.Status, agentEvent.EventType),
                    StartedAtUtc = session.StartedAtUtc,
                    UpdatedAtUtc = agentEvent.OccurredAtUtc,
                    Task = session.Task,
                    Timeline = session.Timeline.Concat([agentEvent]).ToList()
                };
            }
        }

        runtimeService.Publish(ToRuntimeEvent(agentEvent));
    }

    private AgentSessionSnapshot GetRequiredSession(string sessionId)
    {
        return GetSession(sessionId)
               ?? throw new InvalidOperationException($"Agent session not found: {sessionId}");
    }

    private static WorkbenchRuntimeEvent ToRuntimeEvent(AgentTimelineEvent agentEvent)
    {
        return new WorkbenchRuntimeEvent
        {
            EventId = agentEvent.EventId,
            SessionId = agentEvent.SessionId,
            SourceKind = WorkbenchRuntimeConstants.SourceKinds.BuiltInAgent,
            SourceId = "agent-orchestrator",
            EventType = agentEvent.EventType,
            NodeId = agentEvent.NodeId,
            Relation = agentEvent.Relation,
            Message = agentEvent.Message,
            OccurredAtUtc = agentEvent.OccurredAtUtc,
            Metadata = CloneMetadata(agentEvent.Metadata)
        };
    }

    private static string ResolveStatus(string currentStatus, string eventType)
    {
        if (string.Equals(currentStatus, AgentSessionConstants.SessionStatus.Cancelled, StringComparison.Ordinal))
            return currentStatus;

        return eventType switch
        {
            WorkbenchRuntimeConstants.EventTypes.TaskStarted => AgentSessionConstants.SessionStatus.Running,
            WorkbenchRuntimeConstants.EventTypes.TaskCompleted => AgentSessionConstants.SessionStatus.Completed,
            WorkbenchRuntimeConstants.EventTypes.TaskFailed => AgentSessionConstants.SessionStatus.Failed,
            _ => currentStatus
        };
    }

    private static AgentTaskRequest CloneRequest(AgentTaskRequest request)
    {
        return new AgentTaskRequest
        {
            Title = request.Title,
            Objective = request.Objective,
            TargetNodeIds = request.TargetNodeIds.ToList(),
            Metadata = CloneMetadata(request.Metadata)
        };
    }

    private static Dictionary<string, string> CloneMetadata(Dictionary<string, string>? metadata)
    {
        return metadata is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(metadata, StringComparer.OrdinalIgnoreCase);
    }

    private static AgentSessionSnapshot Clone(AgentSessionSnapshot source)
    {
        return new AgentSessionSnapshot
        {
            SessionId = source.SessionId,
            Status = source.Status,
            StartedAtUtc = source.StartedAtUtc,
            UpdatedAtUtc = source.UpdatedAtUtc,
            Task = CloneRequest(source.Task),
            Timeline = source.Timeline
                .Select(eventItem => new AgentTimelineEvent
                {
                    EventId = eventItem.EventId,
                    SessionId = eventItem.SessionId,
                    EventType = eventItem.EventType,
                    NodeId = eventItem.NodeId,
                    Relation = eventItem.Relation,
                    Message = eventItem.Message,
                    OccurredAtUtc = eventItem.OccurredAtUtc,
                    Metadata = CloneMetadata(eventItem.Metadata)
                })
                .ToList()
        };
    }
}
