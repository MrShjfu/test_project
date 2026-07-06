using System.Text;
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
    private readonly IChannel _channel;
    private readonly string _consumerId;
    private readonly List<string> _consumerTags = [];

    private RabbitMqEventBus(IConnection connection, IChannel channel, string consumerId)
    {
        _connection = connection;
        _channel = channel;
        _consumerId = consumerId;
    }

    public static async Task<RabbitMqEventBus> ConnectAsync(string uri, string consumerId)
    {
        var factory = new ConnectionFactory { Uri = new Uri(uri) };
        var connection = await factory.CreateConnectionAsync();
        var channel = await connection.CreateChannelAsync(new CreateChannelOptions(
            publisherConfirmationsEnabled: true,
            publisherConfirmationTrackingEnabled: true));
        await channel.ExchangeDeclareAsync(ExchangeName, ExchangeType.Topic, durable: true);
        return new RabbitMqEventBus(connection, channel, consumerId);
    }

    public async Task PublishAsync(EventEnvelope envelope, CancellationToken ct = default)
    {
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
        await _channel.BasicPublishAsync(ExchangeName, envelope.EventType, mandatory: false, props, body, ct);
    }

    public IDisposable Subscribe(string eventType, Func<EventEnvelope, CancellationToken, Task> handler)
    {
        var queueName = $"helm.{eventType}.{_consumerId}";
        _channel.QueueDeclareAsync(queueName, durable: true, exclusive: false, autoDelete: false).GetAwaiter().GetResult();
        _channel.QueueBindAsync(queueName, ExchangeName, eventType).GetAwaiter().GetResult();

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            var envelope = new EventEnvelope(
                Guid.Parse(Encoding.UTF8.GetString((byte[])ea.BasicProperties.Headers!["event_id"]!)),
                Encoding.UTF8.GetString((byte[])ea.BasicProperties.Headers!["event_type"]!),
                Encoding.UTF8.GetString(ea.Body.ToArray()));
            try
            {
                await handler(envelope, CancellationToken.None);
                await _channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
            }
            catch
            {
                // DLQ policy: later hardening step
                await _channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
            }
        };

        var consumerTag = _channel.BasicConsumeAsync(queueName, autoAck: false, consumer).GetAwaiter().GetResult();
        lock (_consumerTags) _consumerTags.Add(consumerTag);
        return new Unsub(_channel, consumerTag, _consumerTags);
    }

    public async ValueTask DisposeAsync()
    {
        await _channel.DisposeAsync();
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
