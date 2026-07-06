using FluentAssertions;
using Helm.Core.Messaging;
using RabbitMQ.Client;
using Xunit;

[Collection("rabbitmq")]
public class RabbitMqEventBusTests(RabbitMqFixture mq)
{
    [Fact]
    public async Task Publish_reaches_subscriber_across_instances()
    {
        await using var pub = await RabbitMqEventBus.ConnectAsync(mq.AmqpUri, "test-pub");
        await using var sub = await RabbitMqEventBus.ConnectAsync(mq.AmqpUri, "test-sub");
        var tcs = new TaskCompletionSource<EventEnvelope>();
        using var _ = sub.Subscribe("CustomerCreated", (e, _) => { tcs.TrySetResult(e); return Task.CompletedTask; });
        var sent = new EventEnvelope(Guid.NewGuid(), "CustomerCreated", """{"name":"x"}""");
        await pub.PublishAsync(sent);
        (await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeEquivalentTo(sent);
    }

    [Fact]
    public async Task Subscribe_declares_queue_with_expected_topology_name()
    {
        await using var sub = await RabbitMqEventBus.ConnectAsync(mq.AmqpUri, "test-sub");
        using var _ = sub.Subscribe("CustomerCreated", (_, _) => Task.CompletedTask);

        // Verify the exact queue name/topology via a raw connection: a passive declare only
        // succeeds if a queue with that exact name already exists.
        var factory = new ConnectionFactory { Uri = new Uri(mq.AmqpUri) };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();

        var act = async () => await channel.QueueDeclarePassiveAsync("helm.CustomerCreated.test-sub");
        await act.Should().NotThrowAsync();
    }
}
