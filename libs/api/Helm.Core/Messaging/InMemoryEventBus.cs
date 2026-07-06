using System.Collections.Concurrent;

namespace Helm.Core.Messaging;

public sealed class InMemoryEventBus : IEventBus
{
    private readonly ConcurrentDictionary<string, List<Func<EventEnvelope, CancellationToken, Task>>> _subs = new();

    public async Task PublishAsync(EventEnvelope e, CancellationToken ct = default)
    {
        if (_subs.TryGetValue(e.EventType, out var handlers))
            foreach (var h in handlers.ToArray()) await h(e, ct); // synchronous dispatch — tests only (ADR-004)
    }

    public IDisposable Subscribe(string eventType, Func<EventEnvelope, CancellationToken, Task> handler)
    {
        var list = _subs.GetOrAdd(eventType, _ => []);
        lock (list) list.Add(handler);
        return new Unsub(() => { lock (list) list.Remove(handler); });
    }

    private sealed class Unsub(Action a) : IDisposable { public void Dispose() => a(); }
}
