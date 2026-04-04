using Dna.Workbench.Models.Agent;

namespace Dna.Workbench.Runtime;

internal sealed class TopologyRuntimeProjectionService : ITopologyRuntimeProjectionService
{
    private readonly object _gate = new();
    private TopologyRuntimeProjectionSnapshot _snapshot = new();

    public TopologyRuntimeProjectionSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            return Clone(_snapshot);
        }
    }

    public void Apply(AgentTimelineEvent runtimeEvent)
    {
        ArgumentNullException.ThrowIfNull(runtimeEvent);

        lock (_gate)
        {
            if (!string.Equals(_snapshot.SessionId, runtimeEvent.SessionId, StringComparison.Ordinal))
                _snapshot = new TopologyRuntimeProjectionSnapshot { SessionId = runtimeEvent.SessionId };

            var nodes = _snapshot.Nodes.ToDictionary(node => node.NodeId, StringComparer.OrdinalIgnoreCase);
            var edges = _snapshot.Edges.ToDictionary(
                edge => BuildEdgeKey(edge.FromNodeId, edge.ToNodeId, edge.Relation),
                StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(runtimeEvent.NodeId))
            {
                nodes[runtimeEvent.NodeId] = new TopologyRuntimeNodeState
                {
                    NodeId = runtimeEvent.NodeId,
                    Caption = runtimeEvent.Message,
                    State = WorkbenchRuntimeConstants.MapNodeState(runtimeEvent.EventType),
                    Heat = ResolveHeat(runtimeEvent.EventType)
                };
            }

            if (!string.IsNullOrWhiteSpace(runtimeEvent.NodeId) &&
                runtimeEvent.Metadata.TryGetValue("fromNodeId", out var fromNodeId) &&
                !string.IsNullOrWhiteSpace(fromNodeId))
            {
                var edgeKey = BuildEdgeKey(fromNodeId, runtimeEvent.NodeId, runtimeEvent.Relation);
                edges[edgeKey] = new TopologyRuntimeEdgeState
                {
                    FromNodeId = fromNodeId,
                    ToNodeId = runtimeEvent.NodeId,
                    Relation = runtimeEvent.Relation ?? string.Empty,
                    State = WorkbenchRuntimeConstants.MapNodeState(runtimeEvent.EventType),
                    Heat = ResolveHeat(runtimeEvent.EventType)
                };
            }

            _snapshot = new TopologyRuntimeProjectionSnapshot
            {
                SessionId = runtimeEvent.SessionId,
                Nodes = nodes.Values.OrderBy(node => node.NodeId, StringComparer.OrdinalIgnoreCase).ToList(),
                Edges = edges.Values.OrderBy(edge => BuildEdgeKey(edge.FromNodeId, edge.ToNodeId, edge.Relation), StringComparer.OrdinalIgnoreCase).ToList(),
                UpdatedAtUtc = runtimeEvent.OccurredAtUtc
            };
        }
    }

    public void Reset(string sessionId)
    {
        lock (_gate)
        {
            if (string.Equals(_snapshot.SessionId, sessionId, StringComparison.Ordinal))
                _snapshot = new TopologyRuntimeProjectionSnapshot();
        }
    }

    private static double ResolveHeat(string eventType)
    {
        return eventType switch
        {
            WorkbenchAgentConstants.EventTypes.TaskCompleted => 0.35,
            WorkbenchAgentConstants.EventTypes.TaskFailed => 1.0,
            WorkbenchAgentConstants.EventTypes.MemoryRead => 0.4,
            WorkbenchAgentConstants.EventTypes.MemoryWritten => 0.6,
            WorkbenchAgentConstants.EventTypes.KnowledgeUpdated => 0.75,
            _ => 0.5
        };
    }

    private static string BuildEdgeKey(string fromNodeId, string toNodeId, string? relation)
        => $"{fromNodeId}->{toNodeId}:{relation ?? string.Empty}";

    private static TopologyRuntimeProjectionSnapshot Clone(TopologyRuntimeProjectionSnapshot source)
    {
        return new TopologyRuntimeProjectionSnapshot
        {
            SessionId = source.SessionId,
            UpdatedAtUtc = source.UpdatedAtUtc,
            Nodes = source.Nodes
                .Select(node => new TopologyRuntimeNodeState
                {
                    NodeId = node.NodeId,
                    Caption = node.Caption,
                    State = node.State,
                    Heat = node.Heat
                })
                .ToList(),
            Edges = source.Edges
                .Select(edge => new TopologyRuntimeEdgeState
                {
                    FromNodeId = edge.FromNodeId,
                    ToNodeId = edge.ToNodeId,
                    Relation = edge.Relation,
                    State = edge.State,
                    Heat = edge.Heat
                })
                .ToList()
        };
    }
}
