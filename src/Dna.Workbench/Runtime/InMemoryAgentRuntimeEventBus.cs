namespace Dna.Workbench.Runtime;

internal sealed class InMemoryAgentRuntimeEventBus : IAgentRuntimeEventBus
{
    private readonly object _gate = new();
    private readonly List<Action<WorkbenchRuntimeEvent>> _handlers = [];

    public void Publish(WorkbenchRuntimeEvent runtimeEvent)
    {
        ArgumentNullException.ThrowIfNull(runtimeEvent);

        Action<WorkbenchRuntimeEvent>[] handlers;
        lock (_gate)
        {
            handlers = _handlers.ToArray();
        }

        foreach (var handler in handlers)
            handler(runtimeEvent);
    }

    public IDisposable Subscribe(Action<WorkbenchRuntimeEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        lock (_gate)
        {
            _handlers.Add(handler);
        }

        return new Subscription(this, handler);
    }

    private void Unsubscribe(Action<WorkbenchRuntimeEvent> handler)
    {
        lock (_gate)
        {
            _handlers.Remove(handler);
        }
    }

    private sealed class Subscription(
        InMemoryAgentRuntimeEventBus owner,
        Action<WorkbenchRuntimeEvent> handler) : IDisposable
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
