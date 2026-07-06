using FluentAssertions;
using Helm.Core.Messaging;
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
}
