namespace Helm.Core.Messaging;

public interface IDomainEvent { Guid EventId { get; } }   // implemented by every event record

public interface IEventBus
{
    Task PublishAsync(EventEnvelope envelope, CancellationToken ct = default);
    // handler receives the envelope; returns when handled. Subscribe is called at startup by consumers.
    IDisposable Subscribe(string eventType, Func<EventEnvelope, CancellationToken, Task> handler);
}
