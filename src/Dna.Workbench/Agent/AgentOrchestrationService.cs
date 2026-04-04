using Dna.Workbench.Contracts;
using Dna.Workbench.Models.Agent;
using Dna.Workbench.Runtime;

namespace Dna.Workbench.Agent;

internal sealed class AgentOrchestrationService : IAgentOrchestrationService, IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, AgentSessionSnapshot> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly IAgentRuntimeEventBus _eventBus;
    private readonly ITopologyRuntimeProjectionService _projectionService;
    private readonly IDisposable _subscription;

    public AgentOrchestrationService(
        IAgentRuntimeEventBus eventBus,
        ITopologyRuntimeProjectionService projectionService)
    {
        _eventBus = eventBus;
        _projectionService = projectionService;
        _subscription = _eventBus.Subscribe(HandleRuntimeEvent);
    }

    public Task<AgentSessionSnapshot> StartSessionAsync(
        AgentTaskRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var session = new AgentSessionSnapshot
        {
            SessionId = Guid.NewGuid().ToString("N"),
            Status = WorkbenchAgentConstants.SessionStatus.Pending,
            StartedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            Task = CloneRequest(request),
            Timeline = []
        };

        lock (_gate)
        {
            _sessions[session.SessionId] = session;
        }

        PublishAndProject(new AgentTimelineEvent
        {
            SessionId = session.SessionId,
            EventType = WorkbenchAgentConstants.EventTypes.TaskStarted,
            Message = string.IsNullOrWhiteSpace(request.Title) ? "Agent session started." : request.Title,
            Metadata = CloneMetadata(request.Metadata)
        });

        foreach (var nodeId in request.TargetNodeIds.Where(static nodeId => !string.IsNullOrWhiteSpace(nodeId)))
        {
            PublishAndProject(new AgentTimelineEvent
            {
                SessionId = session.SessionId,
                EventType = WorkbenchAgentConstants.EventTypes.NodeEntered,
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
                Status = WorkbenchAgentConstants.SessionStatus.Cancelled,
                StartedAtUtc = session.StartedAtUtc,
                UpdatedAtUtc = DateTime.UtcNow,
                Task = session.Task,
                Timeline = session.Timeline
                    .Concat(
                    [
                        new AgentTimelineEvent
                        {
                            SessionId = sessionId,
                            EventType = WorkbenchAgentConstants.EventTypes.TaskFailed,
                            Message = "Agent session cancelled."
                        }
                    ])
                    .ToList()
            };
        }

        _projectionService.Reset(sessionId);
        return Task.FromResult(true);
    }

    public void Dispose()
    {
        _subscription.Dispose();
    }

    private void HandleRuntimeEvent(AgentTimelineEvent runtimeEvent)
    {
        lock (_gate)
        {
            if (!_sessions.TryGetValue(runtimeEvent.SessionId, out var session))
                return;

            _sessions[runtimeEvent.SessionId] = new AgentSessionSnapshot
            {
                SessionId = session.SessionId,
                Status = ResolveStatus(session.Status, runtimeEvent.EventType),
                StartedAtUtc = session.StartedAtUtc,
                UpdatedAtUtc = runtimeEvent.OccurredAtUtc,
                Task = session.Task,
                Timeline = session.Timeline.Concat([runtimeEvent]).ToList()
            };
        }
    }

    private void PublishAndProject(AgentTimelineEvent runtimeEvent)
    {
        _eventBus.Publish(runtimeEvent);
        _projectionService.Apply(runtimeEvent);
    }

    private AgentSessionSnapshot GetRequiredSession(string sessionId)
    {
        return GetSession(sessionId)
               ?? throw new InvalidOperationException($"Agent session not found: {sessionId}");
    }

    private static string ResolveStatus(string currentStatus, string eventType)
    {
        if (string.Equals(currentStatus, WorkbenchAgentConstants.SessionStatus.Cancelled, StringComparison.Ordinal))
            return currentStatus;

        return eventType switch
        {
            WorkbenchAgentConstants.EventTypes.TaskStarted => WorkbenchAgentConstants.SessionStatus.Running,
            WorkbenchAgentConstants.EventTypes.TaskCompleted => WorkbenchAgentConstants.SessionStatus.Completed,
            WorkbenchAgentConstants.EventTypes.TaskFailed => WorkbenchAgentConstants.SessionStatus.Failed,
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
