using Testcontainers.RabbitMq;
using Xunit;

public sealed class RabbitMqFixture : IAsyncLifetime
{
    private readonly RabbitMqContainer _container = new RabbitMqBuilder("rabbitmq:4-management").Build();
    public string AmqpUri => _container.GetConnectionString();
    public Task InitializeAsync() => _container.StartAsync();
    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition("rabbitmq")]
public class RabbitMqCollection : ICollectionFixture<RabbitMqFixture>;
