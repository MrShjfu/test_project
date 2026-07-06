using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Helm.Core.Messaging;

public static class MessagingExtensions
{
    /// <summary>
    /// Registers <see cref="IEventBus"/> per <c>Messaging:Provider</c> (default <c>InMemory</c>).
    /// RabbitMQ registers a lazy singleton: the connection is established on first resolve
    /// (blocking on <see cref="RabbitMqEventBus.ConnectAsync"/> once) rather than at startup,
    /// so a slow/unavailable broker doesn't fail app boot — documented tradeoff, revisit if
    /// startup-time health checks are needed later.
    /// </summary>
    public static IServiceCollection AddHelmMessaging(this IServiceCollection services, IConfiguration config)
    {
        var provider = config["Messaging:Provider"] ?? "InMemory";
        switch (provider)
        {
            case "InMemory":
                services.AddSingleton<IEventBus, InMemoryEventBus>();
                break;

            case "RabbitMQ":
                var uri = config["Messaging:RabbitMq:Uri"]
                    ?? throw new InvalidOperationException("Messaging:RabbitMq:Uri is required when Messaging:Provider=RabbitMQ");
                services.AddSingleton<IEventBus>(_ =>
                    RabbitMqEventBus.ConnectAsync(uri, Environment.MachineName).GetAwaiter().GetResult());
                break;

            case "AzureServiceBus":
                throw new NotImplementedException("infra phase — see spec out-of-scope table");

            default:
                throw new InvalidOperationException($"Unknown Messaging:Provider '{provider}'");
        }

        return services;
    }
}
