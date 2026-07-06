using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Helm.Core.Messaging;

/// <summary>
/// RabbitMQ transport for <see cref="IEventBus"/>. Topology: single durable topic exchange
/// "helm.events"; routing key = event type; each subscription owns a durable queue
/// "helm.{eventType}.{consumerId}" bound to that routing key.
/// </summary>
public sealed class RabbitMqEventBus : IEventBus, IAsyncDisposable
{
    private const string ExchangeName = "helm.events";
    private readonly IConnection _connection;
    private readonly IChannel _publishChannel;
    private readonly IChannel _consumeChannel;
    private readonly string _consumerId;
    private readonly ILogger _logger;
    private readonly List<string> _consumerTags = [];
    private volatile bool _disposed;

    private RabbitMqEventBus(IConnection connection, IChannel publishChannel, IChannel consumeChannel, string consumerId, ILogger logger)
    {
        _connection = connection;
        _publishChannel = publishChannel;
        _consumeChannel = consumeChannel;
        _consumerId = consumerId;
        _logger = logger;
    }

    public static async Task<RabbitMqEventBus> ConnectAsync(string uri, string consumerId, ILogger? logger = null)
    {
        var factory = new ConnectionFactory { Uri = new Uri(uri) };
        var connection = await factory.CreateConnectionAsync();

        // Dedicated channels: publishing (with publisher confirms) and consuming (declare/bind/consume/ack/nack)
        // must not share a channel — per RabbitMQ.Client v7 guidance, interleaving publish and ack/nack frames
        // on one channel risks frame-interleaving issues. Both channels come from the same connection.
        var publishChannel = await connection.CreateChannelAsync(new CreateChannelOptions(
            publisherConfirmationsEnabled: true,
            publisherConfirmationTrackingEnabled: true));
        var consumeChannel = await connection.CreateChannelAsync();

        await publishChannel.ExchangeDeclareAsync(ExchangeName, ExchangeType.Topic, durable: true);
        return new RabbitMqEventBus(connection, publishChannel, consumeChannel, consumerId, logger ?? NullLogger.Instance);
    }

    public async Task PublishAsync(EventEnvelope envelope, CancellationToken ct = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RabbitMqEventBus));

        var props = new BasicProperties
        {
            Persistent = true,
            Headers = new Dictionary<string, object?>
            {
                ["event_id"] = envelope.EventId.ToString(),
                ["event_type"] = envelope.EventType,
            },
        };
        var body = Encoding.UTF8.GetBytes(envelope.PayloadJson);
        await _publishChannel.BasicPublishAsync(ExchangeName, envelope.EventType, mandatory: false, props, body, ct);
    }

    public IDisposable Subscribe(string eventType, Func<EventEnvelope, CancellationToken, Task> handler)
    {
        var queueName = $"helm.{eventType}.{_consumerId}";
        _consumeChannel.QueueDeclareAsync(queueName, durable: true, exclusive: false, autoDelete: false).GetAwaiter().GetResult();
        _consumeChannel.QueueBindAsync(queueName, ExchangeName, eventType).GetAwaiter().GetResult();

        var consumer = new AsyncEventingBasicConsumer(_consumeChannel);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            EventEnvelope envelope;
            try
            {
                var headers = ea.BasicProperties.Headers
                    ?? throw new InvalidOperationException("missing Headers on message");
                var eventId = Guid.Parse(Encoding.UTF8.GetString((byte[])headers["event_id"]!));
                var eventType2 = Encoding.UTF8.GetString((byte[])headers["event_type"]!);
                envelope = new EventEnvelope(eventId, eventType2, Encoding.UTF8.GetString(ea.Body.ToArray()));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Malformed message on queue {QueueName} (delivery tag {DeliveryTag}): missing or invalid event_id/event_type header. Nacking without requeue.",
                    queueName, ea.DeliveryTag);
                await _consumeChannel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
                return;
            }

            try
            {
                // no ambient token on consumer dispatch; shutdown cancellation is not observable by handlers (accepted)
                await handler(envelope, CancellationToken.None);
                await _consumeChannel.BasicAckAsync(ea.DeliveryTag, multiple: false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Handler threw for event type {EventType} (event id {EventId}) on queue {QueueName}. Nacking without requeue.",
                    envelope.EventType, envelope.EventId, queueName);
                // DLQ policy: later hardening step
                await _consumeChannel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
            }
        };

        var consumerTag = _consumeChannel.BasicConsumeAsync(queueName, autoAck: false, consumer).GetAwaiter().GetResult();
        lock (_consumerTags) _consumerTags.Add(consumerTag);
        return new Unsub(_consumeChannel, consumerTag, _consumerTags);
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;

        string[] activeTags;
        lock (_consumerTags) activeTags = _consumerTags.ToArray();
        foreach (var tag in activeTags)
        {
            try
            {
                await _consumeChannel.BasicCancelAsync(tag);
            }
            catch
            {
                // best-effort cancellation during shutdown; swallow per-tag failures
            }
        }

        await _publishChannel.DisposeAsync();
        await _consumeChannel.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private sealed class Unsub(IChannel channel, string consumerTag, List<string> tags) : IDisposable
    {
        public void Dispose()
        {
            // block-on-async: IDisposable has no async counterpart here; acceptable for shutdown path.
            channel.BasicCancelAsync(consumerTag).GetAwaiter().GetResult();
            lock (tags) tags.Remove(consumerTag);
        }
    }
}
