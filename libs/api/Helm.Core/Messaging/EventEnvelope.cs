namespace Helm.Core.Messaging;

public record EventEnvelope(Guid EventId, string EventType, string PayloadJson);
