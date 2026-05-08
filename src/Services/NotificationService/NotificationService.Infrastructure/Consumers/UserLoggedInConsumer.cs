using Hdos.Common.Messaging;
using Hdos.Contracts.IntegrationEvents;
using Hdos.NotificationService.Application.EventHandlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hdos.NotificationService.Infrastructure.Consumers;

public sealed class UserLoggedInConsumer
    : RabbitMqConsumerHostedService<UserLoggedInIntegrationEvent, UserLoggedInEventHandler>
{
    public UserLoggedInConsumer(
        RabbitMqConnection connection,
        IOptions<RabbitMqOptions> options,
        IServiceScopeFactory scopeFactory,
        ILogger<UserLoggedInConsumer> logger)
        : base(connection, options, scopeFactory, logger,
            queueName: "notification.user-logged-in")
    { }
}

public sealed class UserRegisteredConsumer
    : RabbitMqConsumerHostedService<UserRegisteredIntegrationEvent, UserRegisteredEventHandler>
{
    public UserRegisteredConsumer(
        RabbitMqConnection connection,
        IOptions<RabbitMqOptions> options,
        IServiceScopeFactory scopeFactory,
        ILogger<UserRegisteredConsumer> logger)
        : base(connection, options, scopeFactory, logger,
            queueName: "notification.user-registered")
    { }
}

public sealed class OrderCreatedConsumer
    : RabbitMqConsumerHostedService<OrderCreatedIntegrationEvent, OrderCreatedEventHandler>
{
    public OrderCreatedConsumer(
        RabbitMqConnection connection,
        IOptions<RabbitMqOptions> options,
        IServiceScopeFactory scopeFactory,
        ILogger<OrderCreatedConsumer> logger)
        : base(connection, options, scopeFactory, logger,
            queueName: "notification.order-created")
    { }
}
