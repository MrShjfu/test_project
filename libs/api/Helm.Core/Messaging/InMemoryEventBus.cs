using System.Collections.Concurrent;

namespace Helm.Core.Messaging;

public sealed class InMemoryEventBus : IEventBus
{
    private readonly ConcurrentDictionary<string, List<Func<EventEnvelope, CancellationToken, Task>>> _subs = new();

    public async Task PublishAsync(EventEnvelope e, CancellationToken ct = default)
    {
        if (_subs.TryGetValue(e.EventType, out var handlers))
        {
            Func<EventEnvelope, CancellationToken, Task>[] snapshot;
            lock (handlers) snapshot = handlers.ToArray();
            foreach (var h in snapshot) await h(e, ct); // synchronous dispatch — tests only (ADR-004)
        }
    }

    public IDisposable Subscribe(string eventType, Func<EventEnvelope, CancellationToken, Task> handler)
    {
        var list = _subs.GetOrAdd(eventType, _ => []);
        lock (list) list.Add(handler);
        return new Unsub(() => { lock (list) list.Remove(handler); });
    }

    private sealed class Unsub(Action a) : IDisposable { public void Dispose() => a(); }
}
