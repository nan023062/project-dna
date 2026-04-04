using Dna.Workbench.Models.Agent;

namespace Dna.Workbench.Runtime;

internal sealed class InMemoryAgentRuntimeEventBus : IAgentRuntimeEventBus
{
    private readonly object _gate = new();
    private readonly List<Action<AgentTimelineEvent>> _handlers = [];

    public void Publish(AgentTimelineEvent runtimeEvent)
    {
        ArgumentNullException.ThrowIfNull(runtimeEvent);

        Action<AgentTimelineEvent>[] handlers;
        lock (_gate)
        {
            handlers = _handlers.ToArray();
        }

        foreach (var handler in handlers)
            handler(runtimeEvent);
    }

    public IDisposable Subscribe(Action<AgentTimelineEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        lock (_gate)
        {
            _handlers.Add(handler);
        }

        return new Subscription(this, handler);
    }

    private void Unsubscribe(Action<AgentTimelineEvent> handler)
    {
        lock (_gate)
        {
            _handlers.Remove(handler);
        }
    }

    private sealed class Subscription(
        InMemoryAgentRuntimeEventBus owner,
        Action<AgentTimelineEvent> handler) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            owner.Unsubscribe(handler);
        }
    }
}
