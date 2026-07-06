using FluentAssertions;
using Helm.Core.Messaging;
using Xunit;

public class InMemoryEventBusTests
{
    [Fact]
    public async Task Delivers_to_matching_subscriber_only()
    {
        var bus = new InMemoryEventBus();
        var got = new List<string>();
        using var _ = bus.Subscribe("CustomerCreated", (e, _) => { got.Add(e.EventType); return Task.CompletedTask; });
        await bus.PublishAsync(new EventEnvelope(Guid.NewGuid(), "CustomerCreated", "{}"));
        await bus.PublishAsync(new EventEnvelope(Guid.NewGuid(), "OrderPlaced", "{}"));
        got.Should().Equal("CustomerCreated");
    }
}
